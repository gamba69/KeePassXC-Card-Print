using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentIcons.Common;
using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.ViewModels;
using QRCoder;

namespace KeePassXCCardPrint;

internal static class ScannerResultDialog
{
    private static uint _ownedClipboardSequence;
    internal static void ClearOwnedClipboard() => ScannerNative.TryClearClipboard(_ownedClipboardSequence);
    internal static string T(string ru, string uk, string en) => Localizer.ResolveLanguage(UiText.Language) switch { "en" => en, "uk" => uk, _ => ru };

    internal static async Task ShowAsync(Window owner, ScannerPayload p, UiSettings settings)
    {
        var title = p.Kind switch
        {
            ScannerKind.Totp => T("Одноразовый пароль (TOTP)", "Одноразовий пароль (TOTP)", "One-time password (TOTP)"),
            ScannerKind.Passkey => "Passkey",
            ScannerKind.Url => T("Веб-адрес", "Вебадреса", "Web address"),
            ScannerKind.Binary => T("Бинарные данные", "Двійкові дані", "Binary data"),
            _ => T("Текстовые данные", "Текстові дані", "Text data")
        };
        var win = new Window { Title = title, Width = 550, MinWidth = 390, MaxHeight = 680,
            SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowDecorations = WindowDecorations.Full, ShowInTaskbar = false };
        PrintWindowIcons.Attach(win);
        var root = new DockPanel { LastChildFill = true };
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var footer = new Border { Child = actions }; footer.Classes.Add("app-footer");
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var stack = new StackPanel { Spacing = 8 };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        heading.Children.Add(ActionIconVectors.Create(p.Kind switch { ScannerKind.Totp => Icon.Clock, ScannerKind.Passkey => Icon.Key, ScannerKind.Url => Icon.Link, _ => Icon.DocumentOnePage }, 20));
        heading.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(heading);
        var card = new Border { Margin = new Thickness(12), Padding = new Thickness(12), Child = stack }; card.Classes.Add("settings-card");
        root.Children.Add(new ScrollViewer { Content = card, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        win.Content = root;
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var close = PrintDialogButtons.Create(T("Закрыть", "Закрити", "Close"), "close");
        close.Click += (_, _) => win.Close();
        win.KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; win.Close(); } };
        DispatcherTimer? timer = null;
        Bitmap? qrBitmap = null;
        var secretBoxes = new List<TextBox>();
        var secretTexts = new List<TextBlock>();
        void Action(string text, Icon icon, Func<Task> act)
        {
            var b = PrintDialogButtons.Create(text, ActionIconVectors.Create(icon));
            b.Margin = new Thickness(3, 0, 0, 3);
            b.Click += async (_, _) =>
            {
                b.IsEnabled = false;
                try { await act(); }
                catch { feedback.Text = T("Не удалось выполнить операцию.", "Не вдалося виконати операцію.", "The operation could not be completed."); }
                finally { b.IsEnabled = true; }
            };
            actions.Children.Add(b);
        }
        void Row(string label, string value)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), ColumnSpacing = 8 };
            grid.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
            var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(text, 1); grid.Children.Add(text); stack.Children.Add(grid); secretTexts.Add(text);
        }
        TextBox Reveal(string value, bool hidden, double maxHeight = 180)
        {
            var box = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MaxHeight = maxHeight, Text = hidden ? "••••••••••••" : value };
            secretBoxes.Add(box); stack.Children.Add(box);
            var toggle = new CheckBox { Content = T("Показать данные", "Показати дані", "Show data"), IsChecked = !hidden };
            toggle.IsCheckedChanged += (_, _) => box.Text = toggle.IsChecked == true ? value : "••••••••••••";
            stack.Children.Add(toggle);
            return box;
        }
        async Task Copy(string value)
        {
            if (win.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(value);
            uint sequence = ScannerNative.GetClipboardSequenceNumber();
            _ownedClipboardSequence = sequence;
            _ = ClearClipboardLater(sequence, Math.Clamp(settings.ScannerClipboardSeconds, 5, 300));
            feedback.Text = T("Скопировано. Буфер будет очищен автоматически.", "Скопійовано. Буфер буде очищено автоматично.", "Copied. Clipboard will be cleared automatically.");
        }
        async Task Save(string extension, byte[] bytes)
        {
            if (!await AppConfirmations.ConfirmSecretFileAsync(win, UiText.Language, false)) return;
            var file = await win.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = T("Сохранить данные", "Зберегти дані", "Save data"), SuggestedFileName = "scan." + extension,
                DefaultExtension = extension, FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = ["*." + extension] }]
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0);
            await stream.WriteAsync(bytes);
            feedback.Text = T("Файл сохранён.", "Файл збережено.", "File saved.");
        }
        switch (p.Kind)
        {
            case ScannerKind.Totp:
            {
                Row(T("Сервис", "Сервіс", "Issuer"), p.Issuer);
                Row(T("Учётная запись", "Обліковий запис", "Account"), p.Account);
                var code = new TextBlock { FontSize = 34, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
                secretTexts.Add(code); stack.Children.Add(code);
                var progress = new ProgressBar { Minimum = 0, Maximum = p.Period, Height = 5 }; stack.Children.Add(progress);
                var countdown = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center }; stack.Children.Add(countdown);
                void Tick()
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); int left = p.Period - (int)(now % p.Period);
                    code.Text = p.Code(now); progress.Value = left;
                    countdown.Text = T("До следующего кода", "До наступного коду", "Next code in") + ": " + left + " s";
                }
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) }; timer.Tick += (_, _) => Tick(); Tick(); timer.Start();
                Row(T("Алгоритм", "Алгоритм", "Algorithm"), p.Algorithm + (p.Steam ? " / Steam" : ""));
                Row(T("Длина", "Довжина", "Length"), p.Digits.ToString());
                Row(T("Период", "Період", "Period"), p.Period + " s");
                Reveal(p.Text, settings.ScannerHideSecrets, 100);
                var qr = new Image { Width = 220, Height = 220, IsVisible = false, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
                stack.Children.Add(qr);
                Action(T("Показать / скрыть QR", "Показати / приховати QR", "Show / hide QR"), Icon.QrCode, () =>
                {
                    if (qr.IsVisible) { qr.IsVisible = false; qr.Source = null; qrBitmap?.Dispose(); qrBitmap = null; }
                    else
                    {
                        using var generator = new QRCodeGenerator();
                        using var data = generator.CreateQrCode(p.Text, QRCodeGenerator.ECCLevel.M);
                        using var renderer = new PngByteQRCode(data);
                        byte[] png = renderer.GetGraphic(8);
                        try { using var ms = new MemoryStream(png); qrBitmap = new Bitmap(ms); }
                        finally { CryptographicOperations.ZeroMemory(png); }
                        qr.Source = qrBitmap; qr.IsVisible = true;
                    }
                    return Task.CompletedTask;
                });
                Action(T("Копировать код", "Копіювати код", "Copy code"), Icon.Copy, () => Copy(p.Code(DateTimeOffset.UtcNow.ToUnixTimeSeconds())));
                break;
            }
            case ScannerKind.Passkey:
                Row(T("Пользователь", "Користувач", "User"), p.Account);
                Row("Relying Party", p.Rp);
                Row("Credential ID", p.CredentialId);
                Row("User Handle", p.UserHandle);
                Reveal(p.Text, settings.ScannerHideSecrets);
                Action(T("Сохранить Passkey", "Зберегти Passkey", "Save Passkey"), Icon.Save, () => Save("passkey", p.Bytes));
                break;
            case ScannerKind.Url:
                var uri = new Uri(p.Text);
                Row(T("Протокол", "Протокол", "Scheme"), uri.Scheme.ToUpperInvariant());
                Row(T("Домен", "Домен", "Domain"), uri.IdnHost);
                Reveal(p.Text, settings.ScannerHideSecrets);
                Action(T("Копировать", "Копіювати", "Copy"), Icon.Copy, () => Copy(p.Text));
                Action(T("Открыть", "Відкрити", "Open"), Icon.Open, async () =>
                {
                    if (await AppConfirmations.AskAsync(win, title, T("Открыть адрес во внешнем браузере?", "Відкрити адресу в зовнішньому браузері?", "Open the address in your browser?") + "\n" + uri.IdnHost, T("Открыть", "Відкрити", "Open")))
                        Process.Start(new ProcessStartInfo(p.Text) { UseShellExecute = true });
                });
                break;
            case ScannerKind.Binary:
                Row(T("Размер", "Розмір", "Size"), p.Bytes.Length + " B");
                Reveal(Convert.ToHexString(p.Bytes), settings.ScannerHideSecrets);
                Action(T("Сохранить", "Зберегти", "Save"), Icon.Save, () => Save("bin", p.Bytes));
                break;
            default:
                Row(T("Размер UTF-8", "Розмір UTF-8", "UTF-8 size"), p.Bytes.Length + " B");
                Reveal(p.Text, settings.ScannerHideSecrets);
                Action(T("Копировать", "Копіювати", "Copy"), Icon.Copy, () => Copy(p.Text));
                Action(T("Сохранить", "Зберегти", "Save"), Icon.Save, () => Save("txt", p.Bytes));
                break;
        }
        stack.Children.Add(feedback);
        actions.Children.Add(close);
        try { await win.ShowDialog(owner); }
        finally
        {
            timer?.Stop();
            foreach (var box in secretBoxes) box.Text = "";
            foreach (var block in secretTexts) block.Text = "";
            qrBitmap?.Dispose();
            win.Content = null;
        }
    }

    private static async Task ClearClipboardLater(uint sequence, int seconds)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            // Do not erase clipboard contents copied by another application later.
            for (int i = 0; i < 5 && ScannerNative.GetClipboardSequenceNumber() == sequence; i++)
            {
                if (ScannerNative.TryClearClipboard(sequence)) break;
                await Task.Delay(200);
            }
        }
        catch { /* no logging of clipboard data */ }
    }
}
