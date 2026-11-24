using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Avalonia.Threading;
using KeePassXC.CloudTransport;
using KeePassXCCardPrint.Platforms.Windows.Updates;
using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.Platforms;
using KeePassXCCardPrint.ViewModels;

namespace KeePassXCCardPrint;

public partial class SettingsView : UserControl
{
    private static readonly string[] Providers = ["GoogleDrive", "ProtonDrive", "WebDAV", "S3"];
    private static readonly string[] ProviderNames = ["Google Drive", "Proton Drive", "WebDAV", "S3"];
    private static readonly string[] UpdateModes = ["exit", "ask", "manual"];
    private bool _updating;
    private CloudConfigurationSync? _observed;
    private ShellViewModel? _languageOwner;
    private string? _lastOperationMessage;
    private string? _storageOperationMessage;
    private MainWindow? _storageOwner;
    private bool _unsavedConnection;
    private bool _draftConnectionChanged;
    private bool _syncStoppedForDraft;
    private ProtonCliStatus _protonStatus = new();
    private readonly DispatcherTimer _protonTimer = new() { Interval = TimeSpan.FromHours(24) };
    private bool _protonChecking;
    private bool _cloudCommandRunning;
    internal bool IsCloudCommandRunning => _cloudCommandRunning;
    private string _cloudCommand = "";
    private string _cloudStep = "";
    private CancellationTokenSource? _cloudCommandCancellation;

    public SettingsView()
    {
        InitializeComponent();
        ProviderPicker.ItemsSource = ProviderNames;
        AutoCheck.IsCheckedChanged += (_, _) => SaveUpdaterPreferences();
        AutoDownload.IsCheckedChanged += (_, _) => SaveUpdaterPreferences();
        _protonTimer.Tick += async (_, _) => await CheckProtonAsync(automatic: true);
        AutoProtonCheck.IsCheckedChanged += (_, _) => OnConnectionEdited(null, null);
        AdvancedGoogleCheck.IsCheckedChanged += (_, _) => UpdateProviderFields();
        foreach (var field in new[] { EndpointBox, BucketBox, RegionBox, ClientIdBox,
            ProtonBox, FolderBox, UsernameBox, SecretBox })
            field.TextChanged += OnConnectionEdited;
        DataContextChanged += (_, _) => { ObserveLanguage(); Refresh(); };
        AttachedToVisualTree += (_, _) =>
        {
            ObserveLanguage();
            UpdaterHost.Changed += RefreshOnUi;
            AttachEngine();
            AttachStorageOwner();
            _protonTimer.Start();
            _ = CheckProtonAsync(automatic: true);
            Refresh();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _cloudCommandCancellation?.Cancel();
            _protonTimer.Stop();
            UpdaterHost.Changed -= RefreshOnUi;
            if (_languageOwner is not null)
                _languageOwner.PropertyChanged -= OnLanguageChange;
            _languageOwner = null;
            if (_storageOwner is not null)
                _storageOwner.StorageOperationCompleted -= OnStorageOperationCompleted;
            _storageOwner = null;
            if (_observed is not null) _observed.Changed -= RefreshOnUi;
            _observed = null;
        };
        var saved = CloudConnectionStore.Load();
        var index = Array.IndexOf(Providers, CloudConnectionStore.UiProvider(saved.Provider));
        _updating = true;
        ProviderPicker.SelectedIndex = index;
        EndpointBox.Text = saved.Endpoint;
        BucketBox.Text = saved.Bucket;
        RegionBox.Text = saved.Region;
        ClientIdBox.Text = saved.GoogleClientId;
        ProtonBox.Text = saved.ProtonExecutable;
        FolderBox.Text = saved.RemoteFolder;
        AutoProtonCheck.IsChecked = saved.CheckProtonCliAutomatically;
        _updating = false;
        Refresh();
    }
    private MainWindow? Owner => TopLevel.GetTopLevel(this) as MainWindow;
    private void AttachStorageOwner()
    {
        var owner = Owner;
        if (ReferenceEquals(owner, _storageOwner)) return;
        if (_storageOwner is not null)
            _storageOwner.StorageOperationCompleted -= OnStorageOperationCompleted;
        _storageOwner = owner;
        if (_storageOwner is not null)
            _storageOwner.StorageOperationCompleted += OnStorageOperationCompleted;
    }
    private void OnStorageOperationCompleted(string message, bool succeeded)
    {
        _storageOperationMessage = message;
        if (succeeded) _lastOperationMessage = null;
        RefreshOnUi();
    }
    private string L(string ru, string uk, string en) =>
        Localizer.ResolveLanguage((DataContext as ShellViewModel)?.Settings.Language ?? "auto") switch
        { "uk" => uk, "en" => en, _ => ru };
    private void ObserveLanguage()
    {
        if (ReferenceEquals(_languageOwner, DataContext)) return;
        if (_languageOwner is not null) _languageOwner.PropertyChanged -= OnLanguageChange;
        _languageOwner = DataContext as ShellViewModel;
        if (_languageOwner is not null) _languageOwner.PropertyChanged += OnLanguageChange;
    }
    private void OnLanguageChange(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.SelectedLanguage) or null or "") Refresh();
    }
    private void AttachEngine()
    {
        if (ReferenceEquals(_observed, Owner?.CloudSync)) return;
        if (_observed is not null) _observed.Changed -= RefreshOnUi;
        _observed = Owner?.CloudSync;
        if (_observed is not null) _observed.Changed += RefreshOnUi;
    }
    private void RefreshOnUi()
    {
        try { Dispatcher.UIThread.Post(() =>
        {
            try { if (TopLevel.GetTopLevel(this) is not null) Refresh(); }
            catch (Exception ex) { CloudSyncDiagnostics.Record("ui-refresh", ex); }
        }); }
        catch (Exception ex) { CloudSyncDiagnostics.Record("ui-status-dispatch", ex); }
    }
    private string CloudStepText(string step) => step switch
    {
        "test.connect" => L("Подключение и создание тестовой папки…", "Підключення та створення тестової папки…", "Connecting and preparing test folder…"),
        "test.upload" => L("Отправка тестового файла…", "Надсилання тестового файлу…", "Uploading test object…"),
        "test.download" => L("Загрузка и проверка SHA-256…", "Завантаження та перевірка SHA-256…", "Downloading and verifying SHA-256…"),
        "test.cleanup" => L("Удаление тестового файла…", "Видалення тестового файлу…", "Removing test object…"),
        "test.complete" => L("Доступ подтверждён", "Доступ підтверджено", "Access verified"),
        "sync.prepare" => L("Проверка локальной конфигурации…", "Перевірка локальної конфігурації…", "Inspecting local configuration…"),
        "sync.remote" => L("Проверка облачного архива…", "Перевірка хмарного архіву…", "Checking cloud archive…"),
        "sync.apply" => L("Проверка и применение архива…", "Перевірка та застосування архіву…", "Validating and applying archive…"),
        "sync.upload" => L("Создание архива и отправка…", "Створення архіву та надсилання…", "Creating and uploading archive…"),
        "sync.confirm" => L("Повторное скачивание и проверка SHA-256…", "Повторне завантаження та перевірка SHA-256…", "Downloading again to verify SHA-256…"),
        "sync.destination" => L("Требуется подтверждение новой папки", "Потрібне підтвердження нової папки", "New destination requires confirmation"),
        "auth.open" => L("Авторизация в браузере…", "Авторизація в браузері…", "Authorizing in browser…"),
        _ => L("Ожидание ответа облака…", "Очікування відповіді хмари…", "Waiting for the cloud…")
    };
    // The transport reports named operations, not byte counts. Present phases
    // rather than pretending to know transfer speed or a remaining duration.
    private static (int Current, int Total) CloudActivityStage(string step) => step switch
    {
        "test.connect" => (1, 4),
        "test.upload" => (2, 4),
        "test.download" => (3, 4),
        "test.cleanup" or "test.complete" => (4, 4),
        "sync.prepare" => (1, 4),
        "sync.remote" => (2, 4),
        "sync.apply" or "sync.upload" => (3, 4),
        "sync.confirm" => (4, 4),
        _ => (0, 0)
    };
    private void SetCloudStep(string step)
    {
        if (!_cloudCommandRunning) return;
        _cloudStep = step;
        try { RefreshCloudActivity(); }
        catch (Exception ex) { CloudSyncDiagnostics.Record("ui-progress", ex); }
    }
    private void RefreshCloudActivity()
    {
        TestCloudButtonLabel.Text = _cloudCommandRunning && _cloudCommand ==
            L("Проверка подключения", "Перевірка підключення", "Connection test")
            ? L("Проверяется…", "Перевіряється…", "Checking…")
            : L("Проверить подключение", "Перевірити підключення", "Test connection");
        SyncCloudButtonLabel.Text = _cloudCommandRunning && _cloudCommand ==
            L("Синхронизация", "Синхронізація", "Synchronization")
            ? L("Синхронизация…", "Синхронізація…", "Synchronizing…")
            : L("Синхронизировать сейчас", "Синхронізувати зараз", "Sync now");
        var engine = _observed;
        var background = engine?.Status is "Checking" or "Uploading" or "Downloading";
        CloudActivity.IsVisible = _cloudCommandRunning || background;
        CloudActivityTitle.Text = _cloudCommandRunning ? _cloudCommand :
            L("Фоновая синхронизация", "Фонова синхронізація", "Background synchronization");
        var (current, total) = _cloudCommandRunning ? CloudActivityStage(_cloudStep) : (0, 0);
        var item = engine?.LastTransferProgress;
        if (item is not null && !CloudTransferPresentation.Matches(item, _cloudStep,
                engine?.Status ?? "", _cloudCommandRunning)) item = null;
        var percentage = item is null ? null : CloudTransferPresentation.Percent(item);
        CloudActivityBar.IsVisible = percentage.HasValue || _cloudCommandRunning && total > 0;
        CloudActivityBar.Value = percentage ?? (total == 0 ? 0 : 100d * current / total);
        var description = _cloudCommandRunning
            ? (total == 0 ? "" : L("Этап", "Етап", "Step") + $" {current}/{total} · ") + CloudStepText(_cloudStep)
            : engine?.Status switch
        {
            "Uploading" => L("Отправка конфигурации…", "Надсилання конфігурації…", "Uploading configuration…"),
            "Downloading" => L("Получение и проверка конфигурации…", "Отримання та перевірка конфігурації…", "Downloading and verifying configuration…"),
            _ => L("Проверка подключения…", "Перевірка підключення…", "Checking connection…")
        };
        var language = ViewModels.Localizer.ResolveLanguage((DataContext as ShellViewModel)?.Settings.Language ?? "auto");
        CloudActivityStep.Text = item is null ? description :
            description + " · " + CloudTransferPresentation.Describe(item, language);
    }
    private void SetCloudControlsEnabled(bool enabled)
    {
        foreach (Control item in new Control[] { StoragePicker, ProviderPicker, FolderBox,
            EndpointBox, BucketBox, RegionBox, ClientIdBox, ProtonBox, UsernameBox,
            SecretBox, AdvancedGoogleCheck, AutoProtonCheck, BrowseProtonButton,
            SaveCloudButton, LoginCloudButton, TestCloudButton, SyncCloudButton,
            CloudDestinationButton, RecoverMappingButton }) item.IsEnabled = enabled;
    }
    private void Refresh()
    {
        _updating = true;
        try
        {
            StorageTitle.Text = L("Хранение конфигурации", "Зберігання конфігурації", "Configuration storage");
            StorageCaption.Text = L("Способ хранения", "Спосіб зберігання", "Storage mode");
            ProviderCaption.Text = L("Облачный провайдер", "Хмарний провайдер", "Cloud provider");
            FolderCaption.Text = L("Папка в облаке", "Папка у хмарі", "Cloud folder");
            AdvancedGoogleCheck.Content = L("Дополнительные параметры Google OAuth",
                "Додаткові параметри Google OAuth", "Advanced Google OAuth settings");
            EndpointCaption.Text = L("Адрес сервера (WebDAV / S3 / R2)", "Адреса сервера (WebDAV / S3 / R2)", "Server endpoint (WebDAV / S3 / R2)");
            BucketCaption.Text = "Bucket (S3 / R2)";
            RegionCaption.Text = "Region (S3 / R2)";
            ClientIdCaption.Text = "Google OAuth Client ID";
            BrowseProtonLabel.Text = L("Обзор…", "Огляд…", "Browse…");
            ProtonCaption.Text = L("Путь к Proton CLI (пусто — автопоиск)",
                "Шлях до Proton CLI (порожньо — автопошук)", "Proton CLI path (blank — auto-discover)");
            AutoProtonCheck.Content = L("Автоматически проверять обновления Proton CLI",
                "Автоматично перевіряти оновлення Proton CLI", "Check Proton CLI updates automatically");
            CheckProtonLabel.Text = L("Проверить версию", "Перевірити версію", "Check version");
            InstallProtonLabel.Text = L("Скачать / обновить CLI", "Завантажити / оновити CLI", "Download / update CLI");
            UsernameCaption.Text = L("Имя пользователя / Access Key", "Ім'я користувача / Access Key", "Username / Access Key");
            SecretCaption.Text = L("Пароль / Secret Key / OAuth Client Secret (если изменяется)",
                "Пароль / Secret Key / OAuth Client Secret (якщо змінюється)",
                "Password / Secret Key / OAuth Client Secret (only when changing)");
            SaveCloudButtonLabel.Text = L("Сохранить подключение", "Зберегти підключення", "Save connection");
            LoginCloudButtonLabel.Text = L("Авторизоваться", "Авторизуватися", "Sign in");
            TestCloudButtonLabel.Text = L("Проверить подключение", "Перевірити підключення", "Test connection");
            SyncCloudButtonLabel.Text = L("Синхронизировать сейчас", "Синхронізувати зараз", "Sync now");
            UpdatesCaption.Text = L("Обновления программы", "Оновлення програми", "Application updates");
            MaintenanceCaption.Text = L("Обслуживание", "Обслуговування", "Maintenance");
            ResetSettingsButtonLabel.Text = L("Сбросить настройки…", "Скинути налаштування…", "Reset settings…");
            ResetHelp.Text = L(
                "Создать резервную копию и вернуть настройки к исходным. История печати сохраняется по умолчанию.",
                "Створити резервну копію та скинути налаштування. Історія друку зберігається за замовчуванням.",
                "Back up and reset settings. Print history is preserved by default.");
            ResetSettingsButton.IsEnabled = !_cloudCommandRunning;
            AutoCheck.Content = L("Проверять обновления автоматически", "Автоматично перевіряти оновлення", "Check for updates automatically");
            AutoDownload.Content = L("Скачивать обновления автоматически", "Автоматично завантажувати оновлення", "Download updates automatically");
            InstallModeCaption.Text = L("Установка обновлений", "Установлення оновлень", "Install updates");
            var prefs = UpdaterHost.Service.Preferences;
            AutoCheck.IsChecked = prefs.CheckAutomatically;
            AutoDownload.IsChecked = prefs.DownloadAutomatically;
            AutoDownload.IsEnabled = prefs.CheckAutomatically;
            InstallMode.ItemsSource = new[]
            {
                L("При выходе", "Під час виходу", "On exit"),
                L("Предлагать перезапуск", "Пропонувати перезапуск", "Offer restart"),
                L("Только вручную", "Лише вручну", "Manual only")
            };
            InstallMode.SelectedIndex = Array.IndexOf(UpdateModes, prefs.Installation);
            if (DataContext is ShellViewModel viewModel)
                StoragePicker.SelectedItem = viewModel.SelectedStorage;
            Owner?.RefreshStorageAvailability();
            var blocked = Owner?.CloudStorageBlockReason;
            if (_draftConnectionChanged && SettingsLocation.Mode != ConfigurationStorage.Cloud)
                blocked = L("Сохраните и проверьте изменённое подключение.",
                    "Збережіть і перевірте змінене підключення.",
                    "Save and verify the modified connection.");
            CloudUnavailableReason.IsVisible = !string.IsNullOrEmpty(blocked);
            CloudUnavailableReason.Text = string.IsNullOrEmpty(blocked) ? "" :
                L("Облако недоступно: ", "Хмара недоступна: ", "Cloud unavailable: ") + blocked;
            if (DataContext is ShellViewModel model && _draftConnectionChanged)
                foreach (var option in model.StorageOptions)
                    if (option.Code == "cloud" && SettingsLocation.Mode != ConfigurationStorage.Cloud)
                        option.IsEnabled = false;
            UpdateProviderFields();
            ProtonStatus.Text = _protonStatus.State switch
            {
                "missing" => L("Proton CLI не найден", "Proton CLI не знайдено", "Proton CLI not found"),
                "available" => L("Доступно обновление", "Доступне оновлення", "Update available") +
                    ": " + _protonStatus.Current + " → " + _protonStatus.Latest,
                "current" => L("Proton CLI актуален", "Proton CLI актуальний", "Proton CLI is current") +
                    ": " + _protonStatus.Current,
                "error" => L("Ошибка проверки Proton CLI", "Помилка перевірки Proton CLI", "Proton CLI check failed") +
                    ": " + _protonStatus.Error,
                "checking" => L("Проверка Proton CLI…", "Перевірка Proton CLI…", "Checking Proton CLI…"),
                _ => "Proton CLI"
            };
            AttachEngine();
            var engine = _observed;
            var state = engine?.Status switch
            {
                "Synced" => L("Синхронизировано", "Синхронізовано", "Synced"),
                "Uploading" => L("Загрузка в облако", "Завантаження у хмару", "Uploading"),
                "Downloading" => L("Загрузка из облака", "Завантаження з хмари", "Downloading"),
                "Checking" => L("Проверка подключения", "Перевірка підключення", "Checking connection"),
                "Offline" => L("Нет соединения", "Немає з'єднання", "Offline"),
                "Error" => L("Ошибка", "Помилка", "Error"),
                _ => L("Ожидание", "Очікування", "Pending")
            };
            // Connection verification is separate from active cloud storage.
            // A failed storage switch should not retain a stale sync error.
            var cloudActive = SettingsLocation.Mode == ConfigurationStorage.Cloud;
            var recoveryRoot = CloudMappingRecovery.FindProblemRoot(
                SettingsLocation.DirectoryPath, SettingsLocation.CloudRoot);
            var syncState = cloudActive ? state : L(
                "неактивна (выбрано другое хранилище)",
                "неактивна (вибрано інше сховище)",
                "inactive (another storage mode is selected)");
            CloudStatus.Text = L("Активное хранилище", "Активне сховище", "Active storage") +
                ": " + (DataContext as ShellViewModel)?.StorageOptions.FirstOrDefault(
                    option => option.Code == (SettingsLocation.Mode switch
                    {
                        ConfigurationStorage.Cloud => "cloud",
                        ConfigurationStorage.Local => "local",
                        _ => "appdata"
                    }))?.Label +
                "\n\n" + (CloudConnectionStore.IsCurrentVerified() && !_draftConnectionChanged
                    ? L("Подключение: проверено", "Підключення: перевірено", "Connection: verified")
                    : L("Подключение: не проверено", "Підключення: не перевірено", "Connection: not verified")) +
                "\n" + L("Синхронизация", "Синхронізація", "Synchronization") + ": " + syncState +
                (cloudActive && (engine?.Status is "Error" or "Offline") && !string.IsNullOrEmpty(engine?.Detail) ? " — " + LocalizeSyncError(engine.Detail) : "") +
                (recoveryRoot is null ? "" : "\n" + L(
                    "Несовместимый mapping.json: ",
                    "Несумісний mapping.json: ",
                    "Incompatible mapping.json: ") + Path.Combine(recoveryRoot, "mapping.json") +
                    "\n" + L("Нажмите «Восстановить маппинг…» перед переходом в облако.",
                                 "Натисніть «Відновити мапінг…» перед переходом у хмару.",
                                 "Choose Recover field mapping… before switching to cloud.")) +
                (_unsavedConnection ? "\n" + (_draftConnectionChanged
                    ? L("Параметры подключения изменены; сохраните и проверьте подключение.",
                        "Параметри підключення змінено; збережіть і перевірте підключення.",
                        "Connection settings changed; save and test the connection.")
                    : L("Есть несохранённые настройки.", "Є незбережені налаштування.",
                        "There are unsaved preferences.")) : "");
            CloudOperationStatus.Text = _storageOperationMessage ??
                _lastOperationMessage ?? "";
            CloudOperationStatus.IsVisible = !string.IsNullOrEmpty(CloudOperationStatus.Text);
            CloudDestinationLabel.Text = L("Подтвердить новую папку облака…",
                "Підтвердити нову хмарну папку…", "Confirm new cloud destination…");
            CloudDestinationButton.IsVisible = !_cloudCommandRunning &&
                SettingsLocation.Mode == ConfigurationStorage.Cloud &&
                CloudConnectionStore.IsCurrentVerified() &&
                engine is not null && engine.RequiresDestinationChoice(CloudConnectionStore.Load());
            RecoverMappingLabel.Text = L("Восстановить маппинг…",
                "Відновити мапінг…", "Recover field mapping…");
            RecoverMappingButton.IsVisible = !_cloudCommandRunning && recoveryRoot is not null;
            if (!_cloudCommandRunning)
                SyncCloudButton.IsEnabled = cloudActive && recoveryRoot is null;
            RefreshCloudActivity();
            var last = engine?.LastSuccessfulUtc ?? default;
            LastSync.Text = L("Последняя синхронизация", "Остання синхронізація", "Last sync") + ": " +
                (last == default ? "—" : last.ToLocalTime().ToString("g"));
        }
        finally { _updating = false; }
    }
    private string LocalizeSyncError(string detail)
    {
        if (detail.StartsWith("Both local and cloud configuration changed", StringComparison.Ordinal))
            return L("Конфликт версий. Нажмите «Синхронизировать сейчас» и выберите копию.",
                "Конфлікт версій. Натисніть «Синхронізувати зараз» і оберіть копію.",
                "Configuration conflict. Choose Sync now and select a copy.");
        // Keep the internal exception stable and display one actionable error
        // in every supported language. The filename is safe to expose.
        if (detail.StartsWith("mapping.json:", StringComparison.Ordinal))
            return L("Файл mapping.json имеет несовместимую структуру. Сохраните его копию и восстановите корректный маппинг перед синхронизацией. Данные не перезаписаны.",
                "Файл mapping.json має несумісну структуру. Збережіть його копію та відновіть коректний мапінг перед синхронізацією. Дані не перезаписані.",
                "mapping.json has an incompatible structure. Keep a backup and restore a valid mapping before synchronization. Data has not been overwritten.");
        return detail;
    }

    private CloudConnectionProfile ReadProfile()
    {
        if (ProviderPicker.SelectedIndex is < 0 or >= 4)
            throw new InvalidOperationException(L("Выберите провайдера.", "Оберіть провайдера.", "Select a provider."));
        var endpoint = EndpointBox.Text?.Trim() ?? "";
        return new CloudConnectionProfile
        {
            Provider = CloudConnectionStore.ProviderForSelection(Providers[ProviderPicker.SelectedIndex],
                CloudConnectionStore.Load(), endpoint),
            Endpoint = endpoint,
            Bucket = BucketBox.Text?.Trim() ?? "",
            Region = RegionBox.Text?.Trim() ?? "auto",
            GoogleClientId = ClientIdBox.Text?.Trim() ?? "",
            ProtonExecutable = ProtonBox.Text?.Trim() ?? "",
            RemoteFolder = FolderBox.Text?.Trim() ?? "",
            CheckProtonCliAutomatically = AutoProtonCheck.IsChecked == true
        };
    }
    private void PersistProfile()
    {
        var profile = ReadProfile();
        if (!string.IsNullOrWhiteSpace(SecretBox.Text))
        {
            var target = profile.Provider == "GoogleDrive"
                ? CloudConnectionStore.GoogleSecretTarget : CloudConnectionStore.CredentialTarget;
            PlatformServices.Current.Cloud.Secrets.Write(target, UsernameBox.Text?.Trim() ?? "", SecretBox.Text);
            CloudConnectionStore.InvalidateVerification();
            _updating = true;
            try { SecretBox.Text = string.Empty; }
            finally { _updating = false; }
        }
        CloudConnectionStore.Save(profile);
        _unsavedConnection = false;
        _draftConnectionChanged = false;
        if (_syncStoppedForDraft && SettingsLocation.Mode == ConfigurationStorage.Cloud &&
            CloudConnectionStore.IsCurrentVerified())
        {
            _syncStoppedForDraft = false;
            Owner?.CloudSync.Start();
        }
    }
    private void OnConnectionEdited(object? sender, EventArgs? args)
    {
        if (_updating) return;
        // Editing a field must not erase the last verified configuration. An
        // untouched or reverted draft restores its existing verified state.
        try
        {
            var saved = CloudConnectionStore.Load();
            var draft = ReadProfile();
            var credentialEdited = !string.IsNullOrWhiteSpace(SecretBox.Text);
            _draftConnectionChanged = !CloudConnectionStore.SameConnection(saved, draft) || credentialEdited;
            _unsavedConnection = !CloudConnectionStore.SameProfile(saved, draft) || credentialEdited;
        }
        catch (Exception)
        {
            // Incomplete input is an unsaved draft, never a persisted change.
            _unsavedConnection = true;
            _draftConnectionChanged = true;
        }
        if (SettingsLocation.Mode == ConfigurationStorage.Cloud && Owner is { } owner)
        {
            if (_draftConnectionChanged)
            {
                if (!_syncStoppedForDraft) owner.CloudSync.Stop();
                _syncStoppedForDraft = true;
            }
            else if (_syncStoppedForDraft && CloudConnectionStore.IsCurrentVerified())
            {
                _syncStoppedForDraft = false;
                owner.CloudSync.Start();
            }
        }
        Refresh();
    }
    // Both selectors invoke the same storage transition directly. Neither
    // simulates a selection in the other control.
    private async void OnStorageChanged(object? sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged is async void, so every access (including Refresh)
        // belongs inside an exception boundary, not merely the awaited call.
        try
        {
            if (_updating || _cloudCommandRunning ||
                StoragePicker.SelectedItem is not OptionItem option) return;
            var owner = Owner;
            if (owner is null) return;
            if (option.Code == "cloud" && (_draftConnectionChanged ||
                owner.CloudStorageBlockReason is not null))
            {
                var notice = owner.CloudStorageBlockReason ?? L(
                    "Сохраните и проверьте изменённое подключение.",
                    "Збережіть і перевірте змінене підключення.",
                    "Save and verify the modified connection.");
                _storageOperationMessage = notice;
                Refresh();
                return;
            }
            await owner.SwitchStorageFromSettingsAsync(option);
        }
        catch (Exception ex)
        {
            StorageSwitchSafety.BestEffort(() => CloudSyncDiagnostics.Record("storage-selection-ui", ex));
            StorageSwitchSafety.BestEffort(() => _storageOperationMessage = L(
                "Ошибка выбора хранилища. См. cloud-sync-errors.log.",
                "Помилка вибору сховища. Див. cloud-sync-errors.log.",
                "Storage selection error. See cloud-sync-errors.log."));
        }
        finally
        {
            StorageSwitchSafety.BestEffort(Refresh,
                ex => StorageSwitchSafety.BestEffort(
                    () => CloudSyncDiagnostics.Record("storage-selection-refresh", ex)));
        }
    }

    private async Task ShowUnverifiedCloudAsync(MainWindow owner, string notice)
    {
        try
        {
            // The tab can be detached while the modal is being opened.
            if (TopLevel.GetTopLevel(this) != owner || !owner.IsVisible) return;
            await AppConfirmations.AskAsync(owner,
                L("Облачное хранение", "Хмарне зберігання", "Cloud storage"),
                notice, L("Понятно", "Зрозуміло", "OK"),
                cancelText: L("Закрыть", "Закрити", "Close"));
        }
        catch (Exception ex) { CloudSyncDiagnostics.Record("storage-selection-modal", ex); }
    }
    private void UpdateProviderFields()
    {
        var provider = ProviderPicker.SelectedIndex;
        var endpoint = provider is 2 or 3;
        var bucket = provider == 3;
        var google = provider == 0;
        var proton = provider == 1;
        EndpointCaption.IsVisible = EndpointBox.IsVisible = endpoint;
        BucketCaption.IsVisible = BucketBox.IsVisible = bucket;
        RegionCaption.IsVisible = RegionBox.IsVisible = bucket;
        AdvancedGoogleCheck.IsVisible = google;
        ClientIdCaption.IsVisible = ClientIdBox.IsVisible = google && AdvancedGoogleCheck.IsChecked == true;
        ProtonCaption.IsVisible = ProtonBox.IsVisible = proton;
        BrowseProtonButton.IsVisible = proton;
        AutoProtonCheck.IsVisible = ProtonStatus.IsVisible = ProtonActions.IsVisible = proton;
        UsernameCaption.IsVisible = UsernameBox.IsVisible = endpoint;
        SecretCaption.IsVisible = SecretBox.IsVisible = endpoint ||
            (google && AdvancedGoogleCheck.IsChecked == true);
        LoginCloudButton.IsVisible = google || proton;
    }
    private void OnProviderChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateProviderFields();
        if (!_updating) OnConnectionEdited(sender, EventArgs.Empty);
    }
    private void SaveUpdaterPreferences()
    {
        if (_updating) return;
        UpdaterHost.Service.SavePreferences(UpdaterHost.Service.Preferences with
        { CheckAutomatically = AutoCheck.IsChecked == true,
          DownloadAutomatically = AutoDownload.IsChecked == true });
        Refresh();
    }
    private void OnInstallModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updating || InstallMode.SelectedIndex is < 0 or > 2) return;
        UpdaterHost.Service.SavePreferences(UpdaterHost.Service.Preferences with
        { Installation = UpdateModes[InstallMode.SelectedIndex] });
    }
    private async void OnResetSettings(object? sender, RoutedEventArgs e)
    {
        ResetStatus.IsVisible = true;
        ResetStatus.Text = L("Открытие сброса настроек…",
            "Відкриття скидання налаштувань…", "Opening settings reset…");
        if (_cloudCommandRunning)
        {
            ResetStatus.Text = L("Дождитесь завершения операции.",
                "Дочекайтеся завершення операції.", "Wait for the current operation.");
            return;
        }
        if (Owner is not { } owner)
        {
            ResetStatus.Text = L("Не удалось открыть окно сброса настроек.",
                "Не вдалося відкрити вікно скидання.", "Could not open settings reset.");
            return;
        }
        ResetSettingsButton.IsEnabled = false;
        try
        {
            var options = await new SettingsResetDialog(
                (DataContext as ShellViewModel)?.Settings.Language ?? "auto")
                .ShowDialog<SettingsResetOptions?>(owner);
            if (options is not { } selected)
            {
                ResetStatus.IsVisible = false;
                return;
            }
            var message = L(
                selected.AllStorages ? "Сбросить ВСЕ локальные настройки?" : "Сбросить настройки текущего хранилища?",
                selected.AllStorages ? "Скинути ВСІ локальні налаштування?" : "Скинути налаштування поточного сховища?",
                selected.AllStorages ? "Reset ALL local settings?" : "Reset current storage settings?") +
                "\n\n" + (selected.ClearPrintHistory
                    ? L("ВСЯ папка print-tracking будет удалена во всех хранилищах и в облаке.",
                        "УСЮ папку print-tracking буде видалено в усіх сховищах і хмарі.",
                        "The ENTIRE print-tracking folder will be cleared in all storages and cloud.")
                    : L("Вся папка print-tracking сохранится, включая историю и статусы.",
                        "Уся папка print-tracking збережеться, включно з історією і статусами.",
                        "The entire print-tracking folder, including history and statuses, will be kept.")) +
                "\n\n" + L(
                    "Будет создана резервная копия. Облако перезапишется чистой конфигурацией. При ошибке облака сброс не завершится. Продолжить?",
                    "Буде створено резервну копію. Хмару буде замінено чистою конфігурацією. За помилки хмари скидання не завершиться. Продовжити?",
                    "A backup will be created and the cloud configuration replaced. A cloud failure prevents completing the reset. Continue?");
            if (!await AppConfirmations.AskAsync(owner,
                L("Подтверждение сброса", "Підтвердження скидання", "Confirm settings reset"),
                message, L("Сбросить", "Скинути", "Reset"), sensitive: true,
                actionIcon: "warning", cancelText: L("Отмена", "Скасувати", "Cancel")))
            {
                ResetStatus.IsVisible = false;
                return;
            }
            ResetStatus.Text = L("Запуск сброса настроек и перезапуска…",
                "Запуск скидання та перезапуску…", "Starting reset and restart…");
            owner.ScheduleSettingsReset(selected);
        }
        catch (Exception ex)
        {
            CloudSyncDiagnostics.Record("settings-reset-launch", ex);
            ResetStatus.Text = L("Сброс не запущен. См. cloud-sync-errors.log.",
                "Скидання не запущено. Див. cloud-sync-errors.log.",
                "Reset did not start. See cloud-sync-errors.log.");
        }
        finally
        {
            if (owner.IsVisible) ResetSettingsButton.IsEnabled = true;
        }
    }

    private void OnSaveConnection(object? sender, RoutedEventArgs e)
    {
        if (_cloudCommandRunning) return;
        try { PersistProfile(); _lastOperationMessage = L("Подключение сохранено.",
            "Підключення збережено.", "Connection saved."); }
        catch (Exception ex) { _lastOperationMessage = ex.Message; }
        Refresh();
    }
    private async void OnAuthenticate(object? sender, RoutedEventArgs e)
        => await PerformAsync(async (ct, progress) =>
        {
            PersistProfile();
            if (Owner is not { } owner) throw new InvalidOperationException("Cloud window is unavailable.");
            progress.Report("auth.open");
            await owner.CloudSync.AuthenticateConnectionAsync(ReadProfile(),
                (DataContext as ShellViewModel)?.Settings.Language ?? "auto",
                ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark, ct);
        }, L("Авторизация", "Авторизація", "Authorization"));

    private async void OnTestConnection(object? sender, RoutedEventArgs e)
        => await PerformAsync(async (ct, progress) =>
        {
            PersistProfile();
            if (Owner is not { } owner) throw new InvalidOperationException("Cloud window is unavailable.");
            // Testing verifies only connectivity/read/write/delete. It MUST NOT
            // download configuration.zip or ask which copy wins.
            await owner.CloudSync.VerifyConnectionAsync(ReadProfile(), progress, ct);
            if (SettingsLocation.Mode == ConfigurationStorage.Cloud)
            {
                _syncStoppedForDraft = false;
                owner.CloudSync.Start(); // Safe destination guard runs before any write.
            }
        }, L("Проверка подключения", "Перевірка підключення", "Connection test"),
        L("Подключение проверено.", "Підключення перевірено.", "Connection verified."));

    // A broken mapping is never discarded during synchronization. The user
    // explicitly chooses a known-good prior mapping or a reset, after backup.
    private async void OnRecoverMapping(object? sender, RoutedEventArgs e)
    {
        if (_cloudCommandRunning || Owner is not { } owner) return;
        var root = CloudMappingRecovery.FindProblemRoot(SettingsLocation.DirectoryPath,
            SettingsLocation.CloudRoot);
        if (root is null) return;
        var damagedPath = Path.Combine(root, "mapping.json");
        var previous = CloudMappingRecovery.FindPreviousMapping(root,
            [SettingsLocation.AppDataRoot, SettingsLocation.LocalRoot, SettingsLocation.CloudRoot]);
        var hasBackup = previous is not null;
        var description = hasBackup
            ? L("Повреждённый файл:\n" + damagedPath + "\n\nНайдена корректная копия:\n" + previous +
                    "\n\nВосстановить её? Исходный mapping.json будет сохранён отдельно. Архив в облаке не изменится.",
                "Пошкоджений файл:\n" + damagedPath + "\n\nЗнайдено коректну копію:\n" + previous +
                    "\n\nВідновити її? Початковий mapping.json буде збережено окремо. Архів у хмарі не зміниться.",
                "Damaged file:\n" + damagedPath + "\n\nA valid copy was found:\n" + previous +
                    "\n\nRestore it? The original mapping will be backed up. The remote archive will not change.")
            : L("Повреждённый файл:\n" + damagedPath + "\n\nРабочая копия не найдена. Сброс создаст пустой маппинг. Пользовательские правила не восстановятся.\n\nИсходный файл будет сохранён в резервной копии. Продолжить?",
                "Пошкоджений файл:\n" + damagedPath + "\n\nРобочу копію не знайдено. Скидання створить порожній мапінг. Користувацькі правила не відновляться.\n\nПочатковий файл буде збережено в резервній копії. Продовжити?",
                "Damaged file:\n" + damagedPath + "\n\nNo valid copy was found. Reset creates an empty mapping; custom rules cannot be recovered.\n\nThe original file will be backed up. Continue?");
        if (!await AppConfirmations.AskAsync(owner,
            L("Восстановление маппинга", "Відновлення мапінгу", "Mapping recovery"),
            description,
            hasBackup ? L("Восстановить", "Відновити", "Restore") :
                L("Сбросить маппинг", "Скинути мапінг", "Reset mapping"),
            sensitive: !hasBackup, actionIcon: "save",
            cancelText: L("Отмена", "Скасувати", "Cancel"))) return;
        try
        {
            // Leave automatic synchronization paused until the user has reviewed
            // the restored rules and explicitly synchronized or restarted.
            var backup = await owner.CloudSync.RecoverMappingAsync(previous, root);
            _lastOperationMessage = L("Маппинг восстановлен. Оригинал сохранён: ",
                "Мапінг відновлено. Оригінал збережено: ",
                "Mapping recovered. Original backed up: ") + backup + "\n" +
                L("Перезапустите программу, проверьте правила и запустите синхронизацию вручную.",
                  "Перезапустіть програму, перевірте правила та запустіть синхронізацію вручну.",
                  "Restart the application, review the rules, then synchronize manually.");
        }
        catch (Exception ex)
        {
            CloudSyncDiagnostics.Record("mapping-recovery", ex);
            _lastOperationMessage = L("Не удалось восстановить маппинг: ",
                "Не вдалося відновити мапінг: ", "Could not recover mapping: ") + ex.Message;
        }
        Refresh();
    }

    private async void OnSyncNow(object? sender, RoutedEventArgs e)
        => await PerformAsync(async (ct, progress) =>
        {
            if (Owner?.CloudSync is not { } sync) throw new InvalidOperationException("Cloud sync is unavailable.");
            if (!CloudConnectionStore.IsCurrentVerified())
                throw new InvalidOperationException(L("Сначала проверьте подключение.",
                    "Спочатку перевірте підключення.", "Test the connection first."));
            if (SettingsLocation.Mode != ConfigurationStorage.Cloud)
                throw new InvalidOperationException(L("Сначала включите облачное хранение.",
                    "Спочатку увімкніть хмарне зберігання.", "Switch to cloud storage first."));
            if (sync.RequiresDestinationChoice(CloudConnectionStore.Load()))
                throw new InvalidOperationException(L("Сначала подтвердите новую папку облака.",
                    "Спочатку підтвердьте нову хмарну папку.", "Confirm the new cloud destination first."));
            if (CloudMappingRecovery.ReviewRequired && Owner is { } owner &&
                !await AppConfirmations.AskAsync(owner,
                    L("Подтвердить маппинг", "Підтвердити мапінг", "Confirm mapping"),
                    L("Ранее повреждённый маппинг был восстановлен. Вы проверили его правила? Синхронизация может отправить их в облако или заменить более новой облачной копией.",
                      "Раніше пошкоджений мапінг було відновлено. Ви перевірили його правила? Синхронізація може надіслати їх у хмару або замінити новішою хмарною копією.",
                      "The previously damaged mapping was recovered. Have you reviewed its rules? Synchronization may upload it or replace it with a newer cloud copy."),
                    L("Синхронизировать", "Синхронізувати", "Synchronize"),
                    cancelText: L("Отмена", "Скасувати", "Cancel")))
                throw new OperationCanceledException();
            var resumingAfterReview = CloudMappingRecovery.ReviewRequired;
            try { await sync.SynchronizeAsync(ct, progress); }
            catch (CloudSaveConflictException) when (Owner is not null)
            {
                var choice = await CloudConflictDialog.ChooseAsync(Owner!,
                    (DataContext as ShellViewModel)?.Settings.Language ?? "auto");
                if (choice == StorageSwitchAnswer.Cancel) throw new OperationCanceledException();
                if (choice == StorageSwitchAnswer.Yes) await sync.UploadCurrentAsync(ct, progress);
                else await sync.DownloadExistingAsync(ct, progress);
            }
            if (resumingAfterReview && !CloudMappingRecovery.ReviewRequired)
                sync.Start();
        }, L("Синхронизация", "Синхронізація", "Synchronization"));

    private async void OnConfirmDestination(object? sender, RoutedEventArgs e)
        => await PerformAsync(async (ct, progress) =>
        {
            if (Owner is not { } owner) throw new InvalidOperationException("Cloud window is unavailable.");
            if (SettingsLocation.Mode != ConfigurationStorage.Cloud ||
                !CloudConnectionStore.IsCurrentVerified())
                throw new InvalidOperationException("Cloud connection must be verified first.");
            if (!owner.CloudSync.RequiresDestinationChoice(CloudConnectionStore.Load())) return;
            // Only an explicit choice can replace an archive at another destination.
            var answer = await new StorageSwitchDialog(
                (DataContext as ShellViewModel)?.Settings.Language ?? "auto",
                ConfigurationStorage.Cloud, ConfigurationStorage.Cloud,
                changingCloudDestination: true).ShowDialog<StorageSwitchAnswer>(owner);
            if (answer == StorageSwitchAnswer.Cancel) throw new OperationCanceledException();
            if (answer == StorageSwitchAnswer.Yes)
                await owner.CloudSync.UploadCurrentAsync(ct, progress);
            else
                await owner.CloudSync.DownloadExistingAsync(ct, progress);
            owner.CloudSync.Start();
        }, L("Выбор облачной конфигурации", "Вибір хмарної конфігурації", "Choose cloud configuration"));

    private async Task PerformAsync(Func<CancellationToken, IProgress<string>, Task> operation,
        string title, string? successMessage = null)
    {
        if (_cloudCommandRunning) return;
        _cloudCommandRunning = true;
        _cloudCommand = title;
        _cloudStep = "";
        using var cancellation = new CancellationTokenSource();
        _cloudCommandCancellation = cancellation;
        var progress = new Progress<string>(SetCloudStep);
        try
        {
            SetCloudControlsEnabled(false);
            RefreshCloudActivity();
            await operation(cancellation.Token, progress);
            _lastOperationMessage = title + ": " + (successMessage ?? L("Операция завершена.",
                "Операцію завершено.", "Operation completed."));
        }
        catch (OperationCanceledException)
        {
            _lastOperationMessage = L("Операция отменена.", "Операцію скасовано.", "Operation cancelled.");
        }
        catch (Exception ex)
        {
            CloudSyncDiagnostics.Record("interactive-cloud-command", ex);
            _lastOperationMessage = ex.Message;
        }
        finally
        {
            _cloudCommandCancellation = null;
            _cloudCommandRunning = false;
            _cloudCommand = "";
            _cloudStep = "";
            try
            {
                SetCloudControlsEnabled(true);
                Refresh();
            }
            catch (Exception ex) { CloudSyncDiagnostics.Record("ui-command-complete", ex); }
        }
    }
    private async Task CheckProtonAsync(bool automatic)
    {
        if (_protonChecking || Owner is null) return;
        var profile = CloudConnectionStore.Load();
        if (profile.Provider != "ProtonDrive" ||
            automatic && !profile.CheckProtonCliAutomatically) return;
        var executable = automatic ? profile.ProtonExecutable : ProtonBox.Text?.Trim() ?? "";
        _protonChecking = true;
        _protonStatus = new ProtonCliStatus("checking");
        Refresh();
        try
        {
            _protonStatus = await PlatformServices.Current.Cloud.CheckProtonCliAsync(
                executable, CancellationToken.None);
            if (automatic && _protonStatus.State == "available" && Owner is { } owner &&
                await AppConfirmations.AskAsync(owner,
                    L("Обновление Proton CLI", "Оновлення Proton CLI", "Proton CLI update"),
                    L("Доступна новая версия Proton Drive CLI. Скачать и установить с проверкой SHA-512?",
                      "Доступна нова версія Proton Drive CLI. Завантажити та встановити з перевіркою SHA-512?",
                      "A newer Proton Drive CLI is available. Download and install with SHA-512 verification?"),
                    L("Обновить", "Оновити", "Update")))
                await InstallProtonAsync();
        }
        catch (Exception ex) { _protonStatus = new ProtonCliStatus("error", Error: ex.Message); }
        finally { _protonChecking = false; Refresh(); }
    }
    private async void OnBrowseProton(object? sender, RoutedEventArgs e)
    {
        var picker = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (picker is null) return;
        var files = await picker.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Proton Drive CLI", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Proton Drive CLI")
                { Patterns = ["proton-drive.exe"] }]
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is string path)
            ProtonBox.Text = path;
    }
    private async void OnCheckProton(object? sender, RoutedEventArgs e)
        => await CheckProtonAsync(automatic: false);
    private async void OnInstallProton(object? sender, RoutedEventArgs e)
        => await InstallProtonAsync();
    private async Task InstallProtonAsync()
    {
        if (Owner is not { } owner) return;
        var window = new Window
        {
            Title = "KeePassXC Card Print — Proton CLI",
            Width = 440, MinWidth = 400, Height = 180,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };
        var progressBar = new ProgressBar { Minimum = 0, Maximum = 100,
            IsIndeterminate = true, Height = 8 };
        var message = new TextBlock { Text = L("Загрузка Proton CLI…", "Завантаження Proton CLI…",
            "Downloading Proton CLI…"), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(16) };
        panel.Children.Add(message);
        panel.Children.Add(progressBar);
        window.Content = panel;
        string? downloaded = null;
        Exception? failure = null;
        window.Opened += async (_, _) =>
        {
            try
            {
                var status = new Progress<int>(value =>
                {
                    progressBar.IsIndeterminate = value < 0;
                    if (value >= 0) progressBar.Value = value;
                });
                downloaded = await PlatformServices.Current.Cloud.InstallProtonCliAsync(
                    ProtonBox.Text?.Trim() ?? "", status, CancellationToken.None);
            }
            catch (Exception ex) { failure = ex; }
            finally { window.Close(); }
        };
        await window.ShowDialog(owner);
        if (failure is not null)
        {
            _lastOperationMessage = failure.Message;
            Refresh();
            return;
        }
        if (downloaded is not null)
        {
            _protonStatus = await PlatformServices.Current.Cloud.CheckProtonCliAsync(
                downloaded, CancellationToken.None);
            _lastOperationMessage = L("Proton CLI установлен и проверен.",
                "Proton CLI встановлено та перевірено.", "Proton CLI installed and verified.");
            if (!string.IsNullOrWhiteSpace(ProtonBox.Text))
            {
                // Explicit selections remain explicit: never silently redirect them.
                ProtonBox.Text = downloaded;
                PersistProfile();
            }
            CloudConnectionStore.InvalidateVerification();
            Refresh();
        }
    }

}
