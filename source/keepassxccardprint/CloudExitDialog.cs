using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.ViewModels;
using KeePassXC.CloudTransport;

namespace KeePassXCCardPrint;

internal enum CloudExitChoice { Cancel, Confirmed, ExitWithoutSync }

internal static class CloudExitText
{
    internal static string L(string language, string ru, string uk, string en) => Localizer.ResolveLanguage(language) switch
    { "uk" => uk, "en" => en, _ => ru };

    internal static string Activity(string stage, string language) => stage switch
    {
        "Pending" => L(language, "Ожидает отправки", "Очікує надсилання", "Pending cloud save"),
        "Uploading" => L(language, "Отправка в облако…", "Надсилання у хмару…", "Uploading to cloud…"),
        "Downloading" => L(language, "Загрузка из облака…", "Завантаження з хмари…", "Downloading from cloud…"),
        "Verifying" => L(language, "Проверка облачной записи…", "Перевірка хмарного запису…", "Verifying cloud save…"),
        "Success" => L(language, "Облако синхронизировано", "Хмару синхронізовано", "Cloud synchronized"),
        "Conflict" => L(language, "Конфликт версий", "Конфлікт версій", "Cloud conflict"),
        "Error" => L(language, "Облако: ошибка синхронизации", "Хмара: помилка синхронізації", "Cloud sync failed"),
        _ => L(language, "Ожидание синхронизации…", "Очікування синхронізації…", "Waiting for synchronization…")
    };

    // Do not display raw provider exceptions: they can contain paths or HTTP diagnostics.
    internal static string Reason(string detail, string language)
    {
        if (detail.Contains("Both local and cloud", StringComparison.OrdinalIgnoreCase))
            return L(language, "Изменены обе копии. В настройках нажмите «Синхронизировать сейчас» и выберите версию.",
                "Змінено обидві копії. У налаштуваннях натисніть «Синхронізувати зараз» і оберіть версію.",
                "Both copies changed. In Settings, choose Sync now and select a version.");
        if (detail.Contains("destination", StringComparison.OrdinalIgnoreCase))
            return L(language, "Подтвердите облачную папку в настройках.", "Підтвердьте хмарну теку в налаштуваннях.", "Confirm the cloud destination in Settings.");
        if (detail.Contains("Mapping recovery", StringComparison.OrdinalIgnoreCase))
            return L(language, "Необходимо проверить восстановленный маппинг.", "Потрібно перевірити відновлений мапінг.", "Review the recovered mapping first.");
        if (detail.Contains("not verified", StringComparison.OrdinalIgnoreCase))
            return L(language, "Облачное подключение не проверено.", "Хмарне підключення не перевірено.", "The cloud connection is not verified.");
        if (detail.Contains("timeout", StringComparison.OrdinalIgnoreCase) || detail.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return L(language, "Превышено время ожидания облака.", "Перевищено час очікування хмари.", "Cloud operation timed out.");
        if (detail.Contains("verification", StringComparison.OrdinalIgnoreCase))
            return L(language, "Облачная запись не прошла проверку.", "Хмарний запис не пройшов перевірку.", "Cloud write verification failed.");
        return L(language, "Проверьте соединение и состояние облака в настройках. Подробности в cloud-sync-errors.log.",
            "Перевірте з’єднання та стан хмари в налаштуваннях. Подробиці у cloud-sync-errors.log.",
            "Check the connection and cloud status in Settings. See cloud-sync-errors.log for details.");
    }
}

internal sealed class CloudExitDialog : Window
{
    private readonly CloudConfigurationSync _sync;
    private readonly string _language;
    private readonly TextBlock _heading, _description, _stage;
    private readonly ProgressBar _progress;
    private readonly Button _retry, _exit;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _running, _closed;
    private string L(string ru, string uk, string en) => CloudExitText.L(_language, ru, uk, en);

    internal CloudExitDialog(CloudConfigurationSync sync, string language)
    {
        _sync = sync; _language = language;
        Title = L("Сохранение в облако", "Збереження у хмару", "Saving to cloud");
        Width = 550; MinWidth = 500; SizeToContent = SizeToContent.Height; CanResize = false;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.Full;
        PrintWindowIcons.Attach(this);
        _heading = new TextBlock { Text = Title, FontSize = 15, FontWeight = FontWeight.SemiBold };
        _description = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _stage = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _progress = new ProgressBar { IsIndeterminate = true, Height = 5 };
        _retry = PrintDialogButtons.Create(L("Повторить", "Повторити", "Retry"), "reset");
        _exit = PrintDialogButtons.Create(L("Выйти без синхронизации", "Вийти без синхронізації", "Exit without sync"), "warning");
        _exit.Classes.Add("danger-action");
        if (_exit.Content is StackPanel contents)
            foreach (var child in contents.Children)
            {
                if (child is TextBlock text) text.Foreground = Brushes.White;
                if (child is FluentIcons.Avalonia.FluentIcon icon) icon.Foreground = Brushes.White;
            }
        var cancel = PrintDialogButtons.Create(L("Отменить выход", "Скасувати вихід", "Cancel exit"), "cancel");
        _retry.Click += (_, _) => _ = AttemptAsync();
        _exit.Click += (_, _) => { if (!_running) Close(CloudExitChoice.ExitWithoutSync); };
        cancel.Click += (_, _) => Close(CloudExitChoice.Cancel);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(_exit); actions.Children.Add(_retry); actions.Children.Add(cancel);
        var footer = new Border { Child = actions }; footer.Classes.Add("app-footer");
        var body = new StackPanel { Spacing = 10, Margin = new Thickness(16) };
        body.Children.Add(_heading); body.Children.Add(_description); body.Children.Add(_progress); body.Children.Add(_stage);
        var root = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(body);
        Content = root;
        _timer.Tick += (_, _) => RefreshActivity();
        Closed += (_, _) => { _closed = true; _timer.Stop(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(CloudExitChoice.Cancel); } };
        SetWaiting();
    }

    internal Task<CloudExitChoice> RunAsync(Window owner)
    {
        var result = ShowDialog<CloudExitChoice>(owner);
        _ = AttemptAsync();
        return result;
    }

    private void SetWaiting()
    {
        _heading.Text = L("Сохранение в облако", "Збереження у хмару", "Saving to cloud");
        _description.Text = L("Приложение закроется после подтверждения последнего сохранения. Выход можно отменить; синхронизация продолжится.",
            "Програма закриється після підтвердження останнього збереження. Вихід можна скасувати; синхронізація триватиме.",
            "The application will close after the latest save is confirmed. You can cancel exit without interrupting synchronization.");
        _retry.IsVisible = _exit.IsVisible = false;
        _progress.IsVisible = true;
        _stage.Text = CloudExitText.Activity("Idle", _language);
    }

    private void RefreshActivity()
    {
        if (!_running) return;
        var activity = _sync.Activity;
        _stage.Text = CloudExitText.Activity(activity.Stage, _language);
    }

    private async Task AttemptAsync()
    {
        if (_running || _closed) return;
        _running = true; SetWaiting(); _timer.Start();
        try
        {
            // Bound a single exit attempt, never auto-approve an unconfirmed exit.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await Task.Run(() => _sync.FlushPendingAsync(timeout.Token));
            if (!_closed) Close(CloudExitChoice.Confirmed);
        }
        catch (Exception ex)
        {
            CloudSyncDiagnostics.Record("exit-cloud-flush", ex);
            if (_closed) return;
            _heading.Text = L("Не удалось сохранить в облако", "Не вдалося зберегти у хмару", "Cloud save failed");
            _description.Text = L("Облачное сохранение не подтверждено. При выходе изменения останутся в локальном кэше; при следующем запуске программа повторит попытку. Если возник конфликт, необходимо выбрать версию в настройках.",
                "Хмарне збереження не підтверджено. Після виходу зміни залишаться у локальному кеші; під час наступного запуску програма повторить спробу. Якщо виник конфлікт, оберіть версію у налаштуваннях.",
                "The cloud save is not confirmed. On exit, changes remain in the local cache; the next launch retries synchronization. If there is a conflict, choose a version in Settings.");
            _stage.Text = CloudExitText.Reason(ex is OperationCanceledException ? "timed out" : ex.Message, _language);
            _progress.IsVisible = false;
            _retry.IsVisible = _exit.IsVisible = true;
        }
        finally { _running = false; _timer.Stop(); }
    }
}

internal static class CloudConflictDialog
{
    internal static Task<StorageSwitchAnswer> ChooseAsync(Window owner, string language)
    {
        string L(string ru, string uk, string en) => CloudExitText.L(language, ru, uk, en);
        var win = new Window { Title = L("Конфликт облачных версий", "Конфлікт хмарних версій", "Cloud version conflict"),
            Width = 550, SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowDecorations = WindowDecorations.Full };
        PrintWindowIcons.Attach(win);
        var stack = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        stack.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Text = L(
            "После последней подтверждённой синхронизации изменились обе копии. Автоматическая замена запрещена. Выберите, какую копию оставить. Локальная копия перед загрузкой из облака резервируется.",
            "Після останньої підтвердженої синхронізації змінено обидві копії. Автоматичну заміну заборонено. Оберіть, яку копію залишити. Локальну копію перед завантаженням із хмари буде резервовано.",
            "Both copies changed since the last confirmed synchronization. Automatic replacement is blocked. Choose which copy to keep. The local copy is backed up before applying the cloud copy.") });
        var local = PrintDialogButtons.Create(L("Оставить локальную — заменить облако", "Залишити локальну — замінити хмару", "Keep local — replace cloud"),
            CloudActionIcons.Create(FluentIcons.Common.Icon.CloudArrowUp, 16));
        var remote = PrintDialogButtons.Create(L("Оставить облачную — заменить локальную", "Залишити хмарну — замінити локальну", "Keep cloud — replace local"),
            CloudActionIcons.Create(FluentIcons.Common.Icon.CloudArrowDown, 16));
        var cancel = PrintDialogButtons.Create(L("Отмена — ничего не менять", "Скасувати — нічого не змінювати", "Cancel — change nothing"), "cancel");
        local.Click += (_, _) => win.Close(StorageSwitchAnswer.Yes);
        remote.Click += (_, _) => win.Close(StorageSwitchAnswer.No);
        cancel.Click += (_, _) => win.Close(StorageSwitchAnswer.Cancel);
        stack.Children.Add(local); stack.Children.Add(remote); stack.Children.Add(cancel);
        win.Content = stack;
        win.KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; win.Close(StorageSwitchAnswer.Cancel); } };
        return win.ShowDialog<StorageSwitchAnswer>(owner);
    }
}
