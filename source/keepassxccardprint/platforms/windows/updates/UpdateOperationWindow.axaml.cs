using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using KeePassXCCardPrint.ViewModels;
using KeePassXCCardPrint.Services;
using Avalonia.Styling;

namespace KeePassXCCardPrint.Platforms.Windows.Updates;

// One Avalonia surface is used for preparation, the temporary updater, and
// the post-start result. The updater never instantiates MainWindow or opens KDBX.
public sealed partial class UpdateOperationWindow : Window
{
    private readonly string _language;
    private readonly CancellationTokenSource _cancel = new();
    private bool _working;
    private bool _cancelAllowed;
    private bool _resultMode;
    private bool _error;

    internal UpdateOperationWindow(string language, string operation,
        string fromVersion, string toVersion)
    {
        _language = language;
        InitializeComponent();
        ActualThemeVariantChanged += (_, _) => UpdateIcon();
        Opened += (_, _) => UpdateIcon();
        OperationTitle.Text = Text(operation == "repair" ? "repair" : "update");
        OperationVersions.Text = fromVersion == toVersion
            ? fromVersion : fromVersion + " → " + toVersion;
        OperationButton.Content = Text("cancel");
        Closing += (_, e) => { if (_working) e.Cancel = true; };
        SetProgress(new UpdateOperationProgress("preparing"));
    }

    private void UpdateIcon() => BrandArtwork.Apply(OperationIcon,
        ActualThemeVariant == ThemeVariant.Dark, RenderScaling, 32);

    private string Text(string key)
    {
        var language = Localizer.ResolveLanguage(_language);
        return (key, language) switch
        {
            ("repair", "uk") => "Відновлення програми",
            ("repair", "en") => "Application recovery",
            ("repair", _) => "Восстановление приложения",
            ("update", "uk") => "Оновлення програми",
            ("update", "en") => "Application update",
            ("update", _) => "Обновление приложения",
            ("cancel", "uk") => "Скасувати",
            ("cancel", "en") => "Cancel",
            ("cancel", _) => "Отмена",
            ("close", "uk") => "Закрити",
            ("close", "en") => "Close",
            ("close", _) => "Закрыть",
            ("preparing", "uk") => "Підготовка операції…",
            ("preparing", "en") => "Preparing operation…",
            ("preparing", _) => "Подготовка операции…",
            ("checking", "uk") => "Перевірка релізу…",
            ("checking", "en") => "Checking release…",
            ("checking", _) => "Проверка релиза…",
            ("downloading", "uk") => "Завантаження релізу…",
            ("downloading", "en") => "Downloading release…",
            ("downloading", _) => "Загрузка релиза…",
            ("verifying", "uk") => "Перевірка цілісності та підпису…",
            ("verifying", "en") => "Verifying integrity and signature…",
            ("verifying", _) => "Проверка целостности и подписи…",
            ("extracting", "uk") => "Розпакування перевірених файлів…",
            ("extracting", "en") => "Extracting verified files…",
            ("extracting", _) => "Распаковка проверенных файлов…",
            ("handoff", "uk") => "Запуск інсталятора і закриття програми…",
            ("handoff", "en") => "Starting installer and closing the application…",
            ("handoff", _) => "Запуск установщика и закрытие приложения…",
            ("waiting", "uk") => "Очікування завершення програми…",
            ("waiting", "en") => "Waiting for the application to exit…",
            ("waiting", _) => "Ожидание завершения приложения…",
            ("installing", "uk") => "Заміна файлів програми…",
            ("installing", "en") => "Replacing application files…",
            ("installing", _) => "Замена файлов приложения…",
            ("testing", "uk") => "Перевірка нової версії…",
            ("testing", "en") => "Testing the installed version…",
            ("testing", _) => "Проверка установленной версии…",
            ("rollback", "uk") => "Відкат до попередньої версії…",
            ("rollback", "en") => "Rolling back to the previous version…",
            ("rollback", _) => "Откат к предыдущей версии…",
            ("starting", "uk") => "Запуск програми…",
            ("starting", "en") => "Starting the application…",
            ("starting", _) => "Запуск приложения…",
            ("update-complete", "uk") => "Оновлення успішно встановлено",
            ("update-complete", "en") => "Update installed successfully",
            ("update-complete", _) => "Обновление успешно установлено",
            ("repair-complete", "uk") => "Програму успішно відновлено",
            ("repair-complete", "en") => "Application restored successfully",
            ("repair-complete", _) => "Приложение успешно восстановлено",
            ("repair-rollback", "uk") => "Відновлення не вдалося. Попередній стан повернено.",
            ("repair-rollback", "en") => "Recovery failed. The previous state was restored.",
            ("repair-rollback", _) => "Восстановление не удалось. Предыдущее состояние возвращено.",
            ("rolled-back", "uk") => "Оновлення не вдалося. Попередню версію відновлено.",
            ("rolled-back", "en") => "Update failed. The previous version was restored.",
            ("rolled-back", _) => "Обновление не удалось. Предыдущая версия восстановлена.",
            ("failed", "uk") => "Операцію не завершено. Перевірте стан програми.",
            ("failed", "en") => "Operation failed. Check the application state.",
            ("failed", _) => "Операция не завершена. Проверьте состояние приложения.",
            ("cancelled", "uk") => "Операцію скасовано",
            ("cancelled", "en") => "Operation cancelled",
            ("cancelled", _) => "Операция отменена",
            _ => key
        };
    }

    internal void SetProgress(UpdateOperationProgress progress)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        { Dispatcher.UIThread.Post(() => SetProgress(progress)); return; }
        OperationPhase.Text = Text(progress.Phase);
        OperationDetail.Text = progress.Percent is { } percent
            ? percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%" +
              (progress.Detail.Length == 0 ? "" : " · " + progress.Detail)
            : progress.Detail;
        OperationBar.IsIndeterminate = progress.Percent is null;
        if (progress.Percent is { } value) OperationBar.Value = Math.Clamp(value, 0, 100);
    }

    internal void DisableCancellation()
    {
        _cancelAllowed = false;
        OperationButton.IsEnabled = false;
    }

    internal async Task<bool> RunPreparationAsync(Window owner,
        Func<CancellationToken, Task<bool>> operation)
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _cancelAllowed = true;
        var success = false;
        Opened += async (_, _) =>
        {
            _working = true;
            try
            {
                // Opened provides a UI frame before expensive disk/network work.
                await Task.Yield();
                success = await operation(_cancel.Token);
                if (success) { _working = false; Close(); }
                else ShowFailure(Text("failed"));
            }
            catch (OperationCanceledException) when (_cancel.IsCancellationRequested)
            { ShowFailure(Text("cancelled")); }
            catch (Exception ex) { ShowFailure(ex.Message); }
            finally { _working = false; }
        };
        await ShowDialog(owner);
        return success;
    }

    private void ShowFailure(string message)
    {
        _error = true;
        _cancelAllowed = false;
        OperationBar.IsVisible = false;
        OperationPhase.Text = message;
        OperationButton.IsEnabled = true;
        OperationButton.Content = Text("close");
    }

    internal static async Task ShowResultAsync(Window owner, string language, UpdateOperationResult result)
    {
        var window = new UpdateOperationWindow(language, result.Operation,
            result.FromVersion, result.ToVersion);
        window._resultMode = true;
        window._cancelAllowed = false;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var message = result.Status switch
        {
            "installed" => result.Operation == "repair" ? "repair-complete" : "update-complete",
            "rolled-back" when result.Operation == "repair" => "repair-rollback",
            _ => result.Status
        };
        window.OperationPhase.Text = window.Text(message);
        window.OperationDetail.Text = result.Detail;
        window.OperationBar.IsVisible = false;
        window.OperationButton.Content = window.Text("close");
        await window.ShowDialog(owner);
    }

    internal static UpdateOperationWindow Worker(string[] args)
    {
        var stage = args.Length > 2 ? args[2] : "";
        var to = "";
        try { to = File.ReadAllText(Path.Combine(stage, "verified.txt")).Trim().TrimStart('v'); }
        catch (Exception) { }
        var recovery = args.Length > 0 && args[0] == "--recover-update";
        var from = !recovery && args.Length > 6 ? args[6] : AppBuildInfo.Version;
        var operation = !recovery && args.Length > 7 ? args[7] : "update";
        var language = !recovery && args.Length > 8 ? args[8] : "auto";
        if (recovery)
        {
            try
            {
                var kind = File.ReadAllText(Path.Combine(stage, "operation.txt")).Trim();
                if (kind is "update" or "repair") operation = kind;
                from = File.ReadAllText(Path.Combine(stage, "source-version.txt")).Trim();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        var window = new UpdateOperationWindow(language, operation, from, to);
        window._cancelAllowed = false;
        window.OperationButton.IsVisible = false;
        window.Opened += async (_, _) =>
        {
            window._working = true;
            UpdateHelperBootstrap.WorkStarted = true;
            await Task.Yield();
            IProgress<UpdateOperationProgress> feedback =
                new Progress<UpdateOperationProgress>(window.SetProgress);
            int status;
            try
            {
                status = await Task.Run(() => args[0] == "--apply-update"
                    ? WindowsUpdateService.ApplyUpdate(args, feedback.Report)
                    : WindowsUpdateService.ApplyRecovery(args, feedback.Report));
            }
            catch (Exception ex)
            {
                window.SetProgress(new("failed",null,ex.Message));
                status = 1;
            }
            Environment.ExitCode = status;
            window._working = false;
            if (status != 0)
            {
                window.ShowFailure(window.Text("failed"));
                // The result is saved for the next normal startup. Do not keep
                // an unattended on-exit updater running indefinitely.
                await Task.Delay(5000);
            }
            window.Close();
        };
        return window;
    }

    private void OnOperationButton(object? sender, RoutedEventArgs e)
    {
        if (_resultMode || _error) { Close(); return; }
        if (_cancelAllowed) { DisableCancellation(); _cancel.Cancel(); }
    }
}
