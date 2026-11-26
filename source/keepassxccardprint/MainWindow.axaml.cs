using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using System.Diagnostics;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Rendering;
using System.ComponentModel;
using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.Platforms;
using KeePassXCCardPrint.Models;
using KeePassXCCardPrint.ViewModels;
using KeePassXCCardPrint.Platforms.Windows.Updates;

namespace KeePassXCCardPrint;

public partial class MainWindow : Window
{
    private UiSettingsStore _store = new();
    private UiSettings _settings;
    private ShellViewModel _vm;
    private const double MinEditorPane = 490;
    private const double MinPreviewPane = 420;
    private bool _switchingStorage;
    private readonly CloudConfigurationSync _cloudSync = new();
    public CloudConfigurationSync CloudSync => _cloudSync;
    public event Action<string, bool>? StorageOperationCompleted;
    internal string? CloudStorageBlockReason
    {
        get
        {
            if (SettingsLocation.Mode == ConfigurationStorage.Cloud) return null;
            var lang = Localizer.ResolveLanguage(_settings.Language);
            if (_switchingStorage || _vm.Busy)
                return lang switch
                {
                    "uk" => "Зачекайте завершення поточної операції.",
                    "en" => "Wait for the current operation to finish.",
                    _ => "Дождитесь завершения текущей операции."
                };
            if (!CloudConnectionStore.IsCurrentVerified())
                return lang switch
                {
                    "uk" => "Збережіть і перевірте хмарне підключення.",
                    "en" => "Save and verify the cloud connection.",
                    _ => "Сохраните и проверьте облачное подключение."
                };
            if (CloudMappingRecovery.FindProblemRoot(SettingsLocation.DirectoryPath,
                    SettingsLocation.CloudRoot) is not null)
                return lang switch
                {
                    "uk" => "Відновіть несумісний mapping.json у налаштуваннях.",
                    "en" => "Recover the incompatible mapping.json in Settings.",
                    _ => "Восстановите несовместимый mapping.json в настройках."
                };
            return null;
        }
    }
    internal void RefreshStorageAvailability()
    {
        var available = CloudStorageBlockReason is null;
        foreach (var item in _vm.StorageOptions)
            if (item.Code == "cloud")
                item.IsEnabled = available || SettingsLocation.Mode == ConfigurationStorage.Cloud;
    }
    public Task SwitchStorageFromSettingsAsync(OptionItem option) => SwitchStorageAsync(option);
    private readonly IPasswordInputLanguageScope _passwordLayout = PlatformServices.Current.CreatePasswordInputLanguageScope();
    private CancellationTokenSource? _printExportCancellation;
    private long _vaultGeneration;
    private bool _refreshAwaitingPassword;
    private readonly Grid _panels;
    private bool _restored;
    private bool _applyingPlacement;
    private NormalWindowPlacement _lastNormal;
    private WindowIcon? _titleIconWhite;
    private bool _previewPanning;
    private readonly DispatcherTimer _aboutRefreshTimer = new() { Interval=TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _preferencesSaveTimer = new() { Interval=TimeSpan.FromMilliseconds(260) };
    private string? _appliedThemeCode;
    private Point _previewPanStart;
    private Vector _previewPanOrigin;
    private Point _previewOrigin;
    private bool _previewOriginInitialized;
    private bool _updatingPreviewScrollbars;
    private ScaleTransform? _previewScale;
    private TranslateTransform? _previewTranslate;
    private readonly DispatcherTimer _previewZoomTimer=new() { Interval=TimeSpan.FromMilliseconds(16) };
    private double _previewRenderedZoom=1;
    private double _previewTargetZoom=1;
    private Point _previewZoomAnchor;
    private long _previewLastZoomTick;
    private bool _previewFitActive;
    private bool _previewAnimatingFit;
    private Point _previewFitTargetOrigin;


    public MainWindow()
    {
        _settings = _store.Load();
        // Always begin in "Database and records", even if a different tab
        // was selected and persisted during the previous session.
        _settings.ActiveTab = 0;
        _vm = new ShellViewModel(_settings);
        InitializeComponent();
        InitializeTemplatePreviewInteraction();
        _cloudSync.BeforeRemoteApplyAsync = async () => await Dispatcher.UIThread.InvokeAsync(() =>
            !_vm.Busy && !_vm.HasUnsavedTemplateChanges && _printExportCancellation is null &&
            !_cloudExitPreparing && !_cloudExitPrepared);
        _cloudSync.ApplyOnUiAsync = async action => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_vm.Busy || _vm.HasUnsavedTemplateChanges || _printExportCancellation is not null ||
                _cloudExitPreparing || _cloudExitPrepared)
                throw new InvalidOperationException("Finish editing the current template first.");
            action();
        });
        InitializeCloudFooter();
        _cloudSync.RemoteApplied = () => Dispatcher.UIThread.Post(() =>
        {
            try { OnCloudRemoteApplied(); }
            catch (Exception ex)
            {
                CloudSyncDiagnostics.Record("cloud-remote-apply-ui", ex);
                _vm.VaultStatus = Localizer.ResolveLanguage(_settings.Language) switch
                {
                    "uk" => "[CLOUD] Помилка оновлення інтерфейсу. Перезапустіть Card Print.",
                    "en" => "[CLOUD] Error updating the view; restart Card Print.",
                    _ => "[CLOUD] Ошибка обновления интерфейса. Перезапустите Card Print."
                };
            }
        });
        Opened += async (_, _) => await RestoreHyphenationAsync();
        Closed += (_, _) => _cloudSync.Dispose();
        Title = "KeePassXC Card Print  " + AppBuildInfo.Version;
        Opened += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var result = SettingsResetService.ConsumeOutcome();
                if (result is null) return;
                var lang = Localizer.ResolveLanguage(_settings.Language);
                var title = lang switch
                {
                    "uk" => "Скидання налаштувань", "en" => "Settings reset", _ => "Сброс настроек"
                };
                var message = result.Value.Success ? (lang switch
                {
                    "uk" => "Налаштування скинуто. Резервна копія:\n",
                    "en" => "Settings reset. Backup:\n",
                    _ => "Настройки сброшены. Резервная копия:\n"
                }) + result.Value.Message : (lang switch
                {
                    "uk" => "Скидання не завершено. Помилка: ",
                    "en" => "Reset failed. Error: ",
                    _ => "Сброс не завершён. Ошибка: "
                }) + result.Value.Message;
                await AppConfirmations.AskAsync(this, title, message,
                    lang switch { "uk" => "Зрозуміло", "en" => "OK", _ => "Понятно" },
                    cancelText: lang switch { "uk" => "Закрити", "en" => "Close", _ => "Закрыть" });
            }
            catch (Exception ex) { CloudSyncDiagnostics.Record("settings-reset-result", ex); }
        });
        DataContext = _vm;
        RefreshStorageAvailability();
        UiText.Language = _settings.Language;
        UiText.Enable(this, translateText: false);
        _aboutRefreshTimer.Tick += (_, _) => _vm.About.RefreshDiagnostics(RenderScaling);
        _vm.About.OverlayChangeRequested += SetAvaloniaDiagnostics;
        _vm.PropertyChanged += OnShellPropertyChanged;
        Closed += (_, _) =>
        {
            _aboutRefreshTimer.Stop();
        };
        ApplyPreviewToolbarDock();
        RestoreAdaptiveEditorLayout();
        DataContextChanged += (_, _) => RestoreAdaptiveEditorLayout();
        _preferencesSaveTimer.Tick += (_,_) => { _preferencesSaveTimer.Stop(); Persist(); };
        Closed += (_,_) => _preferencesSaveTimer.Stop();
        // A fixed-size canvas owns the viewport coordinates. No ScrollViewer
        // offset reconciliation or layout resize occurs during wheel zoom.
        var previewViewport=this.FindControl<Canvas>("PreviewViewport");
        previewViewport?.AddHandler(InputElement.PointerWheelChangedEvent,
            OnPreviewWheel,RoutingStrategies.Tunnel,handledEventsToo:true);
        var previewPage=this.FindControl<Grid>("PreviewPageHost");
        _previewRenderedZoom=_vm.PreviewZoomPercent/100d;
        _previewTargetZoom=_previewRenderedZoom;
        _previewZoomTimer.Tick+=OnPreviewZoomFrame;
        // Graphics-app shortcuts: Ctrl+1 is actual size, Ctrl+0 fits the page.
        AddHandler(InputElement.KeyDownEvent,OnPreviewShortcutKeyDown,RoutingStrategies.Tunnel);
        Closed+=(_,_)=>_previewZoomTimer.Stop();
        // Switching applications should not keep our temporary password layout
        // active. Reapply only when this window regains focus with the password
        // field still focused (Win11 can preserve focus across activation).
        Deactivated += (_,_) => _passwordLayout.Leave();
        Activated += (_,_) =>
        {
            if (_settings.AutoEnglishPasswordLayout &&
                this.FindControl<TextBox>("PasswordBox")?.IsFocused == true)
                _passwordLayout.Enter();
        };
        if(previewPage is not null)
        {
            _previewScale=new ScaleTransform(_previewRenderedZoom,_previewRenderedZoom);
            _previewTranslate=new TranslateTransform();
            var transform=new TransformGroup();
            transform.Children.Add(_previewScale);
            transform.Children.Add(_previewTranslate);
            previewPage.RenderTransformOrigin=new RelativePoint(0,0,RelativeUnit.Relative);
            previewPage.RenderTransform=transform;
        }
        var horizontal=this.FindControl<ScrollBar>("PreviewHorizontalScrollBar");
        var vertical=this.FindControl<ScrollBar>("PreviewVerticalScrollBar");
        if(horizontal is not null)horizontal.ValueChanged+=OnPreviewHorizontalScroll;
        if(vertical is not null)vertical.ValueChanged+=OnPreviewVerticalScroll;
        _panels = this.FindControl<Grid>("MainPanels")
            ?? throw new InvalidOperationException("MainPanels was not created.");
        // Explicitly keep ordinary system chrome; never enter FullScreen.
        WindowDecorations = WindowDecorations.Full;
        ExtendClientAreaToDecorationsHint = false;
        // Restore the normal window rectangle before applying a maximized state.
        // Then set the initial window state while the HWND is still hidden.
        // Avalonia handles the initial maximized state, preventing
        // a visible Normal -> Maximized flash during startup.
        ApplySafeNormalPlacement();
        if (_settings.Maximized && !AppStartupState.ResetWindowPlacement)
            WindowState = WindowState.Maximized;
        PositionChanged += (_, _) => RememberNormalPlacement();
        SizeChanged += (_, _) => RememberNormalPlacement();
        _vm.PreferencesChanged += PreferencesChanged;
        // Theme=Auto follows the host system theme as well as explicit choices.
        ActualThemeVariantChanged += (_, _) => UpdateAppIcon();
        // DPI can change when moving the window between differently scaled monitors.
        // BrandArtwork.Apply is a no-op unless the nearest native frame changes.
        LayoutUpdated += (_, _) => UpdateAppIcon();
        SetTheme();
        UpdaterHost.Attach(this);
    }

    // Compact, native-AOT-friendly mapping spinner. Keep the editor string model:
    // partially typed invalid text is preserved until the user enters a valid number.
    private static void StepMappingNumber(MappingEditor editor, string field, int direction)
    {
        string current = field switch
        {
            "Order" => editor.Order,
            "SizePct" => editor.SizePct,
            "TrackingPt" => editor.TrackingPt,
            "GroupSize" => editor.GroupSize,
            "GroupSpaces" => editor.GroupSpaces,
            _ => ""
        };
        if (field == "TrackingPt" && string.IsNullOrWhiteSpace(current)) current = "0";
        if (!double.TryParse(current.Replace(',', '.'),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            return;
        (double min, double max, double step) = field switch
        {
            "Order" => (-9999, 9999, 1),
            "SizePct" => (65, 160, 1),
            "TrackingPt" => (-.5, 1.5, .05),
            "GroupSize" => (0, 16, 1),
            "GroupSpaces" => (0, 2, .1),
            _ => (0, 0, 0)
        };
        if (step == 0) return;
        double next = Math.Clamp(Math.Round(value + Math.Sign(direction)*step, 3,
            MidpointRounding.AwayFromZero), min, max);
        // One-character recovery groups are invalid; jump directly across 1.
        if (field == "GroupSize" && next == 1) next = direction > 0 ? 2 : 0;
        string text = field is "Order" or "SizePct" or "GroupSize"
            ? ((int)Math.Round(next)).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : next.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        if (text == current) return;
        switch (field)
        {
            case "Order": editor.Order = text; break;
            case "SizePct": editor.SizePct = text; break;
            case "TrackingPt": editor.TrackingPt = text; break;
            case "GroupSize": editor.GroupSize = text; break;
            case "GroupSpaces": editor.GroupSpaces = text; break;
        }
    }

    private void OnMainTabsSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Keep navigation usable at the narrowest splitter positions.
        _vm.ShowMainTabCaptions = e.NewSize.Width >= 650;
    }

    private bool _sourceWasLoaded;

    private void RestoreAdaptiveEditorLayout()
    {
        if (DataContext is not ShellViewModel) return;
        SetMappingSplit(_settings.MappingListFraction);
        _sourceWasLoaded = _vm.HasRecords;
        SetSourceConnectionExpanded(!_sourceWasLoaded);
        if (this.FindControl<Grid>("TemplateEditorViewport") is { } viewport)
            _vm.SetTemplateViewportHeight(viewport.Bounds.Height);
    }

    private void OnSourceConnectionToggle(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggle) SetSourceConnectionExpanded(toggle.IsChecked == true);
    }

    private void SetSourceConnectionExpanded(bool expanded)
    {
        if (this.FindControl<ToggleButton>("SourceConnectionToggle") is { } toggle)
            toggle.IsChecked = expanded;
        if (this.FindControl<ScrollViewer>("SourceConnectionScroll") is { } scroll)
            scroll.IsVisible = expanded;
        if (this.FindControl<Control>("SourceConnectionExpandedIcon") is { } up)
            up.IsVisible = expanded;
        if (this.FindControl<Control>("SourceConnectionCollapsedIcon") is { } down)
            down.IsVisible = !expanded;
    }

    private void OnSourcePageSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Height > 0 && this.FindControl<ScrollViewer>("SourceConnectionScroll") is { } scroll)
            scroll.MaxHeight = AdaptiveViewport.SourceConnectionHeight(e.NewSize.Height);
    }

    private void OnTemplateViewportSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is ShellViewModel vm) vm.SetTemplateViewportHeight(e.NewSize.Height);
    }

    private void OnMappingFieldScrollSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Horizontal scrolling must not measure the WrapPanel at infinite width.
        if (e.NewSize.Width > 0 && this.FindControl<WrapPanel>("MappingFieldControls") is { } controls)
            controls.Width = Math.Max(359, e.NewSize.Width-20);
    }

    private void SetMappingSplit(double fraction)
    {
        if (this.FindControl<Grid>("MappingPage") is not { } grid) return;
        fraction = AdaptiveViewport.MappingFraction(fraction);
        grid.RowDefinitions[0].Height = new GridLength(fraction, GridUnitType.Star);
        grid.RowDefinitions[2].Height = new GridLength(1-fraction, GridUnitType.Star);
    }

    private void OnMappingSplitterDragCompleted(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (this.FindControl<Grid>("MappingPage") is not { } grid || !grid.IsVisible) return;
            double list = grid.RowDefinitions[0].ActualHeight, editor = grid.RowDefinitions[2].ActualHeight;
            if (list+editor <= 0) return;
            _settings.MappingListFraction = AdaptiveViewport.MappingFraction(list/(list+editor));
            SetMappingSplit(_settings.MappingListFraction);
            _preferencesSaveTimer.Stop();
            _preferencesSaveTimer.Start();
        }, DispatcherPriority.Loaded);
    }

    private void OnPreviewPaneSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is ShellViewModel && e.NewSize.Width > 0) ApplyPreviewToolbarDock();
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, _vm) && e.PropertyName == nameof(ShellViewModel.HasRecords) && !_vm.Busy)
        {
            bool loaded = _vm.HasRecords;
            if (loaded != _sourceWasLoaded) SetSourceConnectionExpanded(!loaded);
            _sourceWasLoaded = loaded;
        }
        if (ReferenceEquals(sender, _vm) && e.PropertyName == nameof(ShellViewModel.PreviewDuplexAvailable))
            Dispatcher.UIThread.Post(ApplyPreviewToolbarDock, DispatcherPriority.Loaded);
        if (e.PropertyName == nameof(ShellViewModel.ActiveTab))
        {
            UpdateAboutRefresh();
            Dispatcher.UIThread.Post(() => UiText.Apply(this, translateText: false), DispatcherPriority.Loaded);
        }
        if (e.PropertyName == nameof(ShellViewModel.SelectedLanguage))
        {
            UiText.Language = _settings.Language;
            UiText.Apply(this, translateText: false);
        }
    }

    // Popups live in independent visual roots. They are localized when opened,
    // rather than altering bound record values or user-controlled mapping labels.
    private void OnRecordContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        foreach (var item in menu.Items.OfType<MenuItem>())
            if (item.Header is string label)
                item.Header = UiText.Tr(label, _settings.Language);
    }

    private void OnExportMenuOpened(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout flyout) return;
        foreach (var item in flyout.Items.OfType<MenuItem>())
            if (item.Header is string label)
                item.Header = UiText.Tr(label, _settings.Language);
    }

    private void OnPreviewSettingsFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is Flyout { Content: Control content })
            UiText.TranslateTooltips(content, _settings.Language);
    }

    private void UpdateAboutRefresh()
    {
        _aboutRefreshTimer.Stop();
        if (_vm.ActiveTab != 3 || !IsVisible) return;
        _vm.About.RefreshDiagnostics(RenderScaling);
        _aboutRefreshTimer.Start();
    }

    private void SetAvaloniaDiagnostics(bool enabled)
    {
        RendererDiagnostics.DebugOverlays = enabled
            ? RendererDebugOverlays.Fps | RendererDebugOverlays.LayoutTimeGraph |
              RendererDebugOverlays.RenderTimeGraph | RendererDebugOverlays.DirtyRects
            : RendererDebugOverlays.None;
    }

    private void OnMappingNumericSpin(object? sender, RoutedEventArgs e)
    {
        if (sender is not RepeatButton { DataContext: MappingEditor { Enabled: true } editor, Tag: string tag }) return;
        var split=tag.LastIndexOf(':');
        if (split < 0 || !int.TryParse(tag[(split+1)..], out int direction)) return;
        StepMappingNumber(editor, tag[..split], direction);
        e.Handled=true;
    }

    private void OnMappingNumericKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down) ||
            sender is not TextBox { DataContext: MappingEditor { Enabled: true } editor, Tag: string field }) return;
        StepMappingNumber(editor, field, e.Key==Key.Up ? 1 : -1);
        e.Handled=true;
    }

    private void OnAppearanceIncrease(object? sender, RoutedEventArgs e)
    {
        if (_vm.CanEditAppearance && (sender as Control)?.DataContext is AppearanceField field)
            field.Nudge(+1);
    }

    private void OnAppearanceDecrease(object? sender, RoutedEventArgs e)
    {
        if (_vm.CanEditAppearance && (sender as Control)?.DataContext is AppearanceField field)
            field.Nudge(-1);
    }

    private async void OnStorageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Avalonia invokes an async-void handler; no exception may escape it.
        try
        {
            if (_switchingStorage || sender is not ComboBox combo ||
                combo.SelectedItem is not OptionItem selected || _vm is null) return;
            await SwitchStorageAsync(selected);
        }
        catch (Exception ex)
        {
            ReportStorageUiFailure("storage-selection-dispatch", ex);
            StorageSwitchSafety.BestEffort(() => _vm.SetStorageMode(SettingsLocation.Mode),
                error => ReportStorageUiFailure("storage-selection-state", error));
            StorageSwitchSafety.BestEffort(() => StorageCombo.SelectedItem = _vm.SelectedStorage,
                error => ReportStorageUiFailure("storage-selection-display", error));
        }
    }

    private async Task SwitchStorageAsync(OptionItem selected)
    {
        if (_switchingStorage || _vm is null) return;
        var target = selected.Code switch
        {
            "local" => ConfigurationStorage.Local,
            "cloud" => ConfigurationStorage.Cloud,
            _ => ConfigurationStorage.AppData
        };
        if (target == SettingsLocation.Mode) return;
        // The selected UI item is only a request. The active mode remains the
        // persisted SettingsLocation.Mode until a successful activation.
        _switchingStorage = true;
        var activated = false;
        try
        {
            if (_vm.Busy)
            {
                _vm.VaultStatus = "Дождитесь завершения открытия базы.";
                return;
            }
            // A cloud switch copies the active source into the cloud cache.
            // Check that source BEFORE any copy or remote operation, otherwise
            // the same malformed mapping.json is reintroduced on every attempt.
            var damagedStorage = target == ConfigurationStorage.Cloud
                ? CloudMappingRecovery.FindProblemRoot(SettingsLocation.DirectoryPath, SettingsLocation.CloudRoot)
                : null;
            if (damagedStorage is not null)
            {
                var lang = Localizer.ResolveLanguage(_settings.Language);
                var message = lang switch
                {
                    "uk" => "У поточному сховищі пошкоджений mapping.json:\n" +
                        Path.Combine(damagedStorage, "mapping.json") +
                        "\n\nПерейдіть до «Налаштування» → «Відновити мапінг…» перед перемиканням у хмару.",
                    "en" => "The current storage contains an incompatible mapping.json:\n" +
                        Path.Combine(damagedStorage, "mapping.json") +
                        "\n\nOpen Settings → Recover field mapping… before switching to cloud.",
                    _ => "В текущем хранилище несовместимый mapping.json:\n" +
                        Path.Combine(damagedStorage, "mapping.json") +
                        "\n\nОткройте «Настройки» → «Восстановить маппинг…» перед переходом в облако."
                };
                if (await AppConfirmations.AskAsync(this,
                    lang switch { "uk" => "Відновлення мапінгу", "en" => "Mapping recovery", _ => "Восстановление маппинга" },
                    message,
                    lang switch { "uk" => "До налаштувань", "en" => "Open Settings", _ => "В настройки" },
                    cancelText: lang switch { "uk" => "Скасувати", "en" => "Cancel", _ => "Отмена" }))
                    _vm.ActiveTab = 2;
                return;
            }
            if (target == ConfigurationStorage.Cloud && !CloudConnectionStore.IsCurrentVerified())
            {
                var lang = Localizer.ResolveLanguage(_settings.Language);
                var message = lang switch
                {
                    "uk" => "Хмарне підключення не налаштовано або не перевірено. Налаштуйте й перевірте його на вкладці «Налаштування».",
                    "en" => "Cloud connection is not configured or verified. Configure and test it in Settings.",
                    _ => "Облачное подключение не настроено или не проверено. Настройте и проверьте его на вкладке «Настройки»."
                };
                _vm.VaultStatus = "[CLOUD] " + message;
                _vm.ActiveTab = 2;
                await AppConfirmations.AskAsync(this,
                    lang switch { "uk" => "Хмарне зберігання", "en" => "Cloud storage", _ => "Облачное хранение" },
                    message, lang switch { "uk" => "Зрозуміло", "en" => "OK", _ => "Понятно" },
                    cancelText: lang switch { "uk" => "Закрити", "en" => "Close", _ => "Закрыть" });
                return;
            }
            var answer = await new StorageSwitchDialog(_settings.Language, target, SettingsLocation.Mode)
                .ShowDialog<StorageSwitchAnswer>(this);
            if (answer == StorageSwitchAnswer.Cancel) return;

            // The source configuration remains available for later switching back.
            if (!await ResolveUnsavedTemplateAsync()) return;
            _vm.SaveVaultPreferences();
            _vm.SaveCardAppearance();
            CaptureLayout();
            _store.Save(_settings);
            var previous = SettingsLocation.DirectoryPath;
            var destination = SettingsLocation.RootFor(target);
            if (target == ConfigurationStorage.Cloud)
            {
                // Always seed the machine-local cache, including private settings.
                // Download only overwrites the explicit portable allow-list.
                ConfigurationTransfer.CopyCurrent(previous, destination);
                if (answer == StorageSwitchAnswer.Yes)
                    await _cloudSync.UploadCurrentAsync();
                else await _cloudSync.DownloadExistingAsync();
            }
            else if (answer == StorageSwitchAnswer.Yes)
                ConfigurationTransfer.CopyCurrent(previous, destination);
            else Directory.CreateDirectory(destination);

            SettingsLocation.Activate(target);
            activated = true;
            if (target == ConfigurationStorage.Cloud) _cloudSync.Start();
            else _cloudSync.Stop();
            // Reload ALL preferences from the selected folder. This includes UI
            // theme/language, mapping, YubiKey choices and selection tokens.
            _vm.PreferencesChanged -= PreferencesChanged;
            _vm.ClearVault();
            _store = new UiSettingsStore();
            _settings = _store.Load();
            _vm = new ShellViewModel(_settings);
            RefreshStorageAvailability();
            _previewZoomTimer.Stop();
            _previewFitActive=false;
            _previewAnimatingFit=false;
            _previewRenderedZoom=_vm.PreviewZoomPercent/100d;
            _previewTargetZoom=_previewRenderedZoom;
            _previewOriginInitialized=false;
            if(_previewScale is not null)
            {
                _previewScale.ScaleX=_previewRenderedZoom;
                _previewScale.ScaleY=_previewRenderedZoom;
            }
            _vm.PreferencesChanged += PreferencesChanged;
            DataContext = _vm;
            _vm.PropertyChanged += OnShellPropertyChanged;
            UiText.Language = _settings.Language;
            UiText.Apply(this, translateText: false);
            ApplyPreviewToolbarDock();
            SetTheme();
            RestorePanelRatio();
            // Initialize the new document position after the new bindings have
            // measured their sheet/card dimensions; never inherit old pan offsets.
            Dispatcher.UIThread.Post(() =>
            {
                var viewport=this.FindControl<Canvas>("PreviewViewport");
                if(viewport is not null)EnsurePreviewOrigin(viewport);
            },DispatcherPriority.Loaded);
            _ = RestoreHyphenationAsync();
            NotifyStorageOperation(StorageOperationText(true, null), true);
        }
        catch (Exception ex)
        {
            ReportStorageUiFailure("storage-mode-switch", ex);
            // Reporting, localization and view updates can also fail. They are
            // not allowed to turn a handled storage error into a fatal crash.
            var message = activated
                ? "Storage activated; UI refresh failed. Restart the application."
                : "Storage switch failed. See cloud-sync-errors.log.";
            StorageSwitchSafety.BestEffort(() => message = StorageOperationText(activated, ex),
                error => ReportStorageUiFailure("storage-error-text", error));
            StorageSwitchSafety.BestEffort(() => _vm.VaultStatus = "[STORAGE] " + message,
                error => ReportStorageUiFailure("storage-error-display", error));
            NotifyStorageOperation(message, activated);
        }
        finally
        {
            // Restore the *persisted* mode, not the user's tentative selection.
            // Each step is isolated: a broken binding must not prevent cleanup.
            try
            {
                StorageSwitchSafety.BestEffort(() => _vm.SetStorageMode(SettingsLocation.Mode),
                    error => ReportStorageUiFailure("storage-restore-model", error));
                StorageSwitchSafety.BestEffort(() => StorageCombo.SelectedItem = _vm.SelectedStorage,
                    error => ReportStorageUiFailure("storage-restore-selector", error));
                StorageSwitchSafety.BestEffort(RefreshStorageAvailability,
                    error => ReportStorageUiFailure("storage-restore-availability", error));
            }
            finally { _switchingStorage = false; }
        }
    }

    private static void ReportStorageUiFailure(string operation, Exception error) =>
        StorageSwitchSafety.BestEffort(() => CloudSyncDiagnostics.Record(operation, error));

    private void NotifyStorageOperation(string message, bool succeeded) =>
        StorageSwitchSafety.Notify(StorageOperationCompleted, message, succeeded,
            error => ReportStorageUiFailure("storage-operation-observer", error));

    private string StorageOperationText(bool activated, Exception? error)
    {
        var lang = Localizer.ResolveLanguage(_settings.Language);
        if (error is null)
            return lang switch
            {
                "uk" => "Сховище успішно перемкнено.",
                "en" => "Storage switched successfully.",
                _ => "Хранилище успешно переключено."
            };
        if (activated)
            return lang switch
            {
                "uk" => "Сховище перемкнено, але оновити інтерфейс не вдалося. Перезапустіть програму.",
                "en" => "Storage switched, but the interface could not be refreshed. Restart the application.",
                _ => "Хранилище переключено, но интерфейс не удалось обновить. Перезапустите программу."
            };
        var reason = error switch
        {
            UnauthorizedAccessException => lang switch
            {
                "uk" => "немає доступу до файлів", "en" => "file access denied", _ => "нет доступа к файлам"
            },
            IOException => lang switch
            {
                "uk" => "помилка запису даних", "en" => "storage I/O error", _ => "ошибка записи данных"
            },
            InvalidOperationException => lang switch
            {
                "uk" => "операцію відхилено", "en" => "operation rejected", _ => "операция отклонена"
            },
            _ => error.GetType().Name
        };
        return lang switch
        {
            "uk" => "Не вдалося перемкнути сховище (" + reason + "). Див. cloud-sync-errors.log.",
            "en" => "Storage switch failed (" + reason + "). See cloud-sync-errors.log.",
            _ => "Не удалось переключить хранилище (" + reason + "). См. cloud-sync-errors.log."
        };
    }

    private void OnCloudRemoteApplied()
    {
        if (SettingsLocation.Mode != ConfigurationStorage.Cloud || _switchingStorage) return;
        // The pre-apply gate prevented replacing a dirty mapping or an active export.
        // Discard the old view-model after saving local-only vault preferences.
        _vm.PreferencesChanged -= PreferencesChanged;
        _vm.PropertyChanged -= OnShellPropertyChanged;
        _vm.ClearVault();
        var active = _vm.ActiveTab;
        _store = new UiSettingsStore();
        _settings = _store.Load();
        _settings.ActiveTab = active;
        _vm = new ShellViewModel(_settings);
        _vm.PreferencesChanged += PreferencesChanged;
        _vm.PropertyChanged += OnShellPropertyChanged;
        _vm.About.OverlayChangeRequested += SetAvaloniaDiagnostics;
        DataContext = _vm;
        UiText.Language = _settings.Language;
        UiText.Apply(this, translateText: false);
        ApplyPreviewToolbarDock();
        SetTheme();
        _ = RestoreHyphenationAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void PreferencesChanged(object? sender, EventArgs args)
    {
        if(_appliedThemeCode != _settings.Theme) SetTheme();
        // Thumb dragging must never synchronously serialize ui-shell.json on
        // every pointer event. Save once after the short burst of changes.
        _preferencesSaveTimer.Stop();
        _preferencesSaveTimer.Start();
    }

    private void SetTheme()
    {
        if (Application.Current is not { } application) return;
        _appliedThemeCode = _settings.Theme;
        application.RequestedThemeVariant = _settings.Theme switch
        {
            "dark" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
        UpdateAppIcon();
    }

    private void UpdateAppIcon()
    {
        // The 48-DIP in-window icon follows the content theme and uses native
        // source pixels at the current DPI, as in KeePassXC Cloud Sync.
        // The OS-level window/taskbar icon always stays white, regardless of theme.
        var logo = this.FindControl<Image>("AppHeaderIcon");
        if (logo is not null)
            BrandArtwork.Apply(logo, ActualThemeVariant == ThemeVariant.Dark, RenderScaling, 48);
        _titleIconWhite ??= LoadTitleIcon("avares://KeePassXCCardPrint/assets/CardPrint_white.ico");
        if (!ReferenceEquals(Icon, _titleIconWhite)) Icon = _titleIconWhite;
    }

    private static WindowIcon LoadTitleIcon(string uri)
    {
        using var stream = AssetLoader.Open(new Uri(uri));
        return new WindowIcon(stream);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateAppIcon();
        // Screen geometry may change while startup is in progress.
        // Recheck geometry only for normal windows; never change maximized state.
        ApplySafeNormalPlacement();
        RestorePanelRatio();
        _restored = true;
        UpdateAboutRefresh();
        if (SettingsLocation.Mode == ConfigurationStorage.Cloud) _cloudSync.Start();
        // Proton CLI release checks run while the application is open,
        // regardless of which tab the user is viewing. Never install silently.
        var protonTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        string lastProtonUpdate = "";
        async Task CheckProtonUpdateAsync()
        {
            try
            {
                var config = CloudConnectionStore.Load();
                if (config.Provider != "ProtonDrive" || !config.CheckProtonCliAutomatically) return;
                var status = await PlatformServices.Current.Cloud.CheckProtonCliAsync(
                    config.ProtonExecutable, CancellationToken.None);
                if (status.State == "available" && status.Latest != lastProtonUpdate)
                {
                    lastProtonUpdate = status.Latest;
                    _vm.VaultStatus = "[CLOUD] Proton Drive CLI " + status.Latest + " — " +
                        (Localizer.ResolveLanguage(_settings.Language) switch
                        {
                            "uk" => "Доступне оновлення. Відкрийте «Налаштування».",
                            "en" => "Update available. Open Settings.",
                            _ => "Доступно обновление. Откройте «Настройки»."
                        });
                }
            }
            catch (Exception) { /* A failed optional check never prevents startup. */ }
        }
        protonTimer.Tick += async (_, _) => await CheckProtonUpdateAsync();
        Closed += (_, _) => protonTimer.Stop();
        protonTimer.Start();
        _ = CheckProtonUpdateAsync();

        // A remembered database path is enough to make password entry the
        // primary startup action. Do not open the vault or persist a secret.
        // Loaded runs after the initial layout: focusing in the constructor
        // or immediately in OnOpened can lose the keyboard focus to TabControl.
        if (!string.IsNullOrWhiteSpace(_vm.DatabasePath))
        {
            Dispatcher.UIThread.Post(() =>
            {
                // If the user already switched tabs, do not steal focus.
                if (!IsVisible || _vm.ActiveTab != 0) return;
                var passwordBox = this.FindControl<TextBox>("PasswordBox");
                if (passwordBox is { IsVisible: true, IsEnabled: true })
                    passwordBox.Focus();
            }, DispatcherPriority.Loaded);
        }
    }


    private void RestorePanelRatio()
    {
        var width = _panels.Bounds.Width;
        if (width <= 0) width = Math.Max(960, Bounds.Width - 16);
        var available = Math.Max(MinEditorPane + MinPreviewPane, width - 6);
        var left = Math.Clamp(_settings.LeftPaneWidth, MinEditorPane, available - MinPreviewPane);
        var right = Math.Max(MinPreviewPane, available - left);
        _panels.ColumnDefinitions[0].Width = new GridLength(left / right, GridUnitType.Star);
        _panels.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
    }

    private void ApplySafeNormalPlacement()
    {
        if (_applyingPlacement || WindowState == WindowState.Maximized) return;
        var area = ChooseWorkArea();
        if (area is null)
        {
            // Screen enumeration failed. These are deliberately small defaults,
            // not the potentially oversized 1670x910 persisted values.
            Width = 1024;
            Height = 660;
            MinWidth = 960;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }
        var savedWidth = AppStartupState.ResetWindowPlacement ? 1100 : _settings.WindowWidth;
        var savedHeight = AppStartupState.ResetWindowPlacement ? 720 : _settings.WindowHeight;
        var savedX = AppStartupState.ResetWindowPlacement ? null : _settings.WindowX;
        var savedY = AppStartupState.ResetWindowPlacement ? null : _settings.WindowY;
        var plan = WindowPlacement.Fit(area.Value, savedWidth, savedHeight, savedX, savedY);
        _applyingPlacement = true;
        try
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            MinWidth = plan.MinWidthDip;
            MinHeight = plan.MinHeightDip;
            Width = plan.WidthDip;
            Height = plan.HeightDip;
            Position = new PixelPoint(plan.X, plan.Y);
            _lastNormal = plan;
        }
        finally { _applyingPlacement = false; }
    }

    private ScreenWorkArea? ChooseWorkArea()
    {
        if (Screens is not { } screens || screens.All.Count == 0) return null;
        var selected = screens.Primary ?? screens.All[0];
        if (!AppStartupState.ResetWindowPlacement && _settings.WindowX is int x && _settings.WindowY is int y)
        {
            // Identify the original monitor in physical desktop coordinates.
            // If it was disconnected, fall back to the primary display.
            foreach (var screen in screens.All)
            {
                var rect = screen.Bounds;
                if (x >= rect.X && x < rect.Right && y >= rect.Y && y < rect.Bottom)
                {
                    selected = screen;
                    break;
                }
            }
        }
        var work = selected.WorkingArea;
        return new ScreenWorkArea(work.X, work.Y, work.Width, work.Height, selected.Scaling);
    }

    private void RememberNormalPlacement()
    {
        if (!_restored || _applyingPlacement || WindowState != WindowState.Normal) return;
        var screen = Screens.ScreenFromWindow(this);
        if (screen is null) return;
        var work = screen.WorkingArea;
        var area = new ScreenWorkArea(work.X, work.Y, work.Width, work.Height, screen.Scaling);
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width < 320 || height < 300) return;
        var candidate = new NormalWindowPlacement(Position.X, Position.Y, width, height,
                                                   MinWidth, MinHeight);
        // An out-of-bounds value is precisely the bug we must NEVER persist.
        if (WindowPlacement.Fits(area, candidate)) _lastNormal = candidate;
    }

    private bool _exitApproved;
    private bool _exitPromptOpen;
    private bool _settingsResetScheduled;
    internal void ScheduleSettingsReset(SettingsResetOptions options)
    {
        if (_settingsResetScheduled) return;
        if (_vm.Busy || _switchingStorage || _printExportCancellation is not null)
            throw new InvalidOperationException("Wait for the current operation to finish before resetting settings.");
        // The worker waits for this process to exit before deleting anything;
        // the normal shutdown save handlers cannot recreate cleared settings.
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Settings reset requires Windows.");
        KeePassXCCardPrint.Platforms.Windows.Storage.WindowsSettingsResetHost.Start(options);
        _settingsResetScheduled = true;
        _exitApproved = true;
        Close();
    }
    internal string UpdateLanguage => _settings.Language;
    internal bool IsExportRunning => _printExportCancellation is not null;
    internal async Task<bool> ReadyForUpdateAsync()
    {
        if (_exitPromptOpen || _cloudExitPreparing || _switchingStorage || _vm.Busy ||
            this.FindControl<SettingsView>("ApplicationSettingsView")?.IsCloudCommandRunning == true)
            return false;
        return await ResolveUnsavedTemplateAsync(exiting:true) && await PrepareCloudExitAsync();
    }
    internal void CloseForUpdate() { _exitApproved=true; Close(); }
    internal void ResumeAfterUpdatePreparation()
    {
        if (!_exitApproved) ResumeCloudAfterCancelledExit();
    }
    private async Task<bool> ResolveUnsavedTemplateAsync(bool exiting=false)
    {
        if(!_vm.HasUnsavedTemplateChanges)return true;
        var decision=await new UnsavedMappingDialog(_settings.Language,exiting)
            .ShowDialog<UnsavedMappingChoice>(this);
        if(decision==UnsavedMappingChoice.Discard)return true;
        if(decision!=UnsavedMappingChoice.Save)return false;
        _vm.SaveChangesToTemplate();
        if(!_vm.HasUnsavedTemplateChanges)return true;
        _vm.NavigateTemplateEditor("mapping"); // Expose invalid mapping drafts for correction.
        return false;
    }
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if(!_exitApproved)
        {
            e.Cancel=true;
            if(_exitPromptOpen || _cloudExitPreparing || UpdaterHost.IsInstalling)return;
            if (_switchingStorage || _vm.Busy ||
                this.FindControl<SettingsView>("ApplicationSettingsView")?.IsCloudCommandRunning == true)
            {
                await AppConfirmations.AskAsync(this, CloudL("Завершение работы", "Завершення роботи", "Exit application"),
                    CloudL("Дождитесь завершения текущей операции.", "Дочекайтеся завершення поточної операції.", "Wait for the current operation to finish."),
                    CloudL("Понятно", "Зрозуміло", "OK"), cancelText: CloudL("Отмена", "Скасувати", "Cancel"));
                return;
            }
            _exitPromptOpen=true;
            try
            {
                bool approved=_vm.HasUnsavedTemplateChanges
                    ? await ResolveUnsavedTemplateAsync(exiting:true)
                    : await AppConfirmations.ConfirmExitAsync(this,_settings.Language,
                        _printExportCancellation is not null);
                if (approved && await PrepareCloudExitAsync())
                {
                    _exitApproved=true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                CloudSyncDiagnostics.Record("safe-exit", ex);
                ResumeCloudAfterCancelledExit();
            }
            finally{_exitPromptOpen=false;}
            return;
        }
        _passwordLayout.Leave();
        _vaultGeneration++;
        _printExportCancellation?.Cancel();
        _preferencesSaveTimer.Stop();
        if (!_settingsResetScheduled && !_cloudExitPrepared)
        {
            _vm.SaveVaultPreferences();
            _vm.SaveCardAppearance();
            CaptureLayout();
            Persist();
        }
        // No portable configuration writes after the verified cloud barrier.
        _vm.ClearVault();
        if (!_settingsResetScheduled) UpdaterHost.InstallOnExitIfReady();
        base.OnClosing(e);
    }

    private void CaptureLayout()
    {
        if (!_restored) return;
        if (WindowState == WindowState.Normal) RememberNormalPlacement();
        _settings.Maximized = WindowState == WindowState.Maximized ||
                              (WindowState == WindowState.Minimized && _settings.Maximized);
        // Never write maximized Bounds as the future restored window bounds.
        // The cached NORMAL rectangle is already validated against work area.
        if (_lastNormal.WidthDip > 0 && _lastNormal.HeightDip > 0)
        {
            _settings.WindowWidth = _lastNormal.WidthDip;
            _settings.WindowHeight = _lastNormal.HeightDip;
            _settings.WindowX = _lastNormal.X;
            _settings.WindowY = _lastNormal.Y;
        }
        _settings.LeftPaneWidth = Math.Max(MinEditorPane, _panels.ColumnDefinitions[0].ActualWidth);
    }

    private void Persist()
    {
        try
        {
            _store.Save(_settings);
            Title = "KeePassXC Card Print  " + AppBuildInfo.Version;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Keep an explicit error; never silently change storage modes.
            Title = "KeePassXC Card Print  ·  UI settings could not be saved";
        }
    }
    private async void OnBrowseDatabase(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title = UiText.Tr("Открыть базу KeePassXC"), AllowMultiple = false,
          FileTypeFilter = [new FilePickerFileType("KeePass KDBX") { Patterns = ["*.kdbx"] }] });
        if (files.Count != 0 && files[0].TryGetLocalPath() is string path) _vm.DatabasePath = path;
    }

    private async void OnBrowseKeyFile(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title = UiText.Tr("Выбрать файл-ключ"), AllowMultiple = false,
          FileTypeFilter =
          [
              new FilePickerFileType("KeePassXC (*.keyx)") { Patterns = ["*.keyx"] },
              new FilePickerFileType(UiText.Tr("Другие файлы ключей")) { Patterns = ["*.key", "*.xml", "*.bin"] },
              new FilePickerFileType(UiText.Tr("Все файлы")) { Patterns = ["*"] }
          ] });
        if (files.Count != 0 && files[0].TryGetLocalPath() is string path) _vm.KeyFilePath = path;
    }

    private async void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
        e.Handled = true;
        bool refresh = _refreshAwaitingPassword && _vm.CanRefreshRecords;
        _refreshAwaitingPassword = false;
        await _vm.OpenVaultAsync(preserveCurrentView: refresh);
    }

    private void OnRecordRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: VaultRecord record } row) return;
        if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
        // The actual checkbox already toggles the bound property on its own.
        for (var current = e.Source as Visual; current is not null; current = current.GetVisualParent())
            if (current is CheckBox) return;
        row.Focus();
        record.Selected = !record.Selected;
        e.Handled = true;
    }

    private void OnRecordRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter) ||
            sender is not Border { DataContext: VaultRecord record }) return;
        // A focused checkbox owns its own Space key; never toggle the row twice.
        for (var current = e.Source as Visual; current is not null; current = current.GetVisualParent())
            if (current is CheckBox) return;
        record.Selected = !record.Selected;
        e.Handled = true;
    }

    private async void OnOpenDatabase(object? sender, RoutedEventArgs e)
    {
        _refreshAwaitingPassword = false;
        await _vm.OpenVaultAsync();
    }
    private void OnLoadDemo(object? sender, RoutedEventArgs e)
    {
        if (_vm.Busy) return;
        _refreshAwaitingPassword = false;
        _passwordLayout.Leave();
        _vaultGeneration++;
        _printExportCancellation?.Cancel();
        _vm.SaveVaultPreferences();
        _vm.SaveCardAppearance();
        _vm.LoadDemo();
    }
    private void OnCloseDatabase(object? sender, RoutedEventArgs e)
    {
        _refreshAwaitingPassword = false;
        _passwordLayout.Leave();
        _vaultGeneration++;
        _printExportCancellation?.Cancel();
        _vm.SaveVaultPreferences();
        _vm.SaveCardAppearance();
        _vm.ClearVault();
    }
    private void OnFilterSelected(object? sender, RoutedEventArgs e)
        => _vm.ToggleRecordQuickFilter(RecordQuickFilter.Selected);
    private void OnFilterRemaining(object? sender, RoutedEventArgs e)
        => _vm.ToggleRecordQuickFilter(RecordQuickFilter.Remaining);
    private void OnFilterReprint(object? sender, RoutedEventArgs e)
        => _vm.ToggleRecordQuickFilter(RecordQuickFilter.Reprint);
    private void OnClearRecordFilters(object? sender, RoutedEventArgs e)
        => _vm.ResetRecordFilters();

    private async void OnRefreshRecords(object? sender, RoutedEventArgs e)
    {
        if (!_vm.CanRefreshRecords) return;
        // No secret is cached merely to enable a one-click database reload.
        // Keep the existing opened snapshot and checkbox state until a new
        // KDBX read has succeeded with the user's renewed credentials.
        if (_vm.RequiresPasswordForRefresh && string.IsNullOrEmpty(_vm.PasswordInput))
        {
            _refreshAwaitingPassword = true;
            _vm.VaultStatus = Localizer.Text(_settings.Language, "QuickRefreshPasswordNeeded");
            this.FindControl<TextBox>("PasswordBox")?.Focus();
            return;
        }
        _refreshAwaitingPassword = false;
        await _vm.OpenVaultAsync(preserveCurrentView: true);
    }

    private void OnSelectAll(object? sender, RoutedEventArgs e) => _vm.SetAll(true);
    private void OnSelectNone(object? sender, RoutedEventArgs e) => _vm.SetAll(false);
    private void OnRefreshYubi(object? sender, RoutedEventArgs e) => _vm.RefreshDeviceList();
    private void OnPasswordGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (_settings.AutoEnglishPasswordLayout) _passwordLayout.Enter();
    }

    private void OnPasswordLostFocus(object? sender, FocusChangedEventArgs e)
    {
        _passwordLayout.Leave();
    }

    private void OnPasswordLayoutToggleClick(object? sender, RoutedEventArgs e)
    {
        // The checkbox itself receives focus when clicked. The layout will be
        // applied on the next focus of the password field if enabled.
        if (!_settings.AutoEnglishPasswordLayout) _passwordLayout.Leave();
    }

    private void OnTogglePassword(object? sender, RoutedEventArgs e)
    {
        var box = this.FindControl<TextBox>("PasswordBox");
        var toggle = this.FindControl<CheckBox>("ShowPasswordToggle");
        if (box is not null) box.PasswordChar = toggle?.IsChecked == true ? '\0' : '●';
    }

    // Screen-space top-left of the document is authoritative. It is never
    // derived from ScrollViewer.Offset (which can be clamped during layout).
    // The image itself stays at its unzoomed size; the Skia vector scene is
    // transformed without recording a new picture or allocating a PNG.
    private void OnPreviewWheel(object? sender,PointerWheelEventArgs e)
    {
        var viewport=this.FindControl<Canvas>("PreviewViewport");
        if(viewport is null || _previewScale is null)return;
        double delta=e.Delta.Y!=0?e.Delta.Y:e.Delta.X;
        if(delta==0)return;
        e.Handled=true;
        EnsurePreviewOrigin(viewport);
        _previewFitActive=false;
        _previewAnimatingFit=false;
        _previewZoomAnchor=e.GetPosition(viewport);
        _vm.ZoomPreview(delta); // fractional trackpad deltas are not quantised
        _previewTargetZoom=_vm.PreviewZoomPercent/100d;
        if(_previewPanning)
        {
            // Wheel always zooms, including while the mouse button is held.
            // Do not let a later animation frame fight the drag anchor.
            _previewZoomTimer.Stop();
            MovePreviewZoom(_previewTargetZoom);
            _previewPanOrigin=new Vector(_previewOrigin.X,_previewOrigin.Y);
            _previewPanStart=_previewZoomAnchor;
            return;
        }
        // Start moving on this very pointer event; interpolate the remainder
        // over a few frames without waiting for a new picture or re-layout.
        MovePreviewZoom(_previewRenderedZoom+(_previewTargetZoom-_previewRenderedZoom)*.24);
        if(Math.Abs(_previewTargetZoom-_previewRenderedZoom)>1e-5)
        {
            _previewLastZoomTick=Stopwatch.GetTimestamp();
            _previewZoomTimer.Start();
        }
        else _previewZoomTimer.Stop();
    }

    private void MovePreviewZoom(double newScale)
    {
        if(_previewScale is null || Math.Abs(newScale-_previewRenderedZoom)<1e-12)return;
        // The document coordinate under the pointer stays fixed throughout animation.
        var nextOrigin=PreviewViewportGeometry.ZoomAtCursor(
            _previewOrigin,_previewZoomAnchor,_previewRenderedZoom,newScale);
        ApplyPreviewZoomTransform(newScale,nextOrigin);
    }

    private void ApplyPreviewZoomTransform(double scale,Point origin)
    {
        if(_previewScale is null)return;
        _previewRenderedZoom=scale;
        _previewOrigin=origin;
        _previewScale.ScaleX=scale;
        _previewScale.ScaleY=scale;
        ApplyPreviewOrigin();
    }

    private void AdvancePreviewZoom(double fraction)
    {
        double next=_previewRenderedZoom+(_previewTargetZoom-_previewRenderedZoom)*fraction;
        if(Math.Abs(next-_previewTargetZoom)<.0001)next=_previewTargetZoom;
        if(_previewAnimatingFit)
        {
            var p=new Point(
                _previewOrigin.X+(_previewFitTargetOrigin.X-_previewOrigin.X)*fraction,
                _previewOrigin.Y+(_previewFitTargetOrigin.Y-_previewOrigin.Y)*fraction);
            if(next==_previewTargetZoom &&
               Math.Abs(p.X-_previewFitTargetOrigin.X)<.1 &&
               Math.Abs(p.Y-_previewFitTargetOrigin.Y)<.1)
                p=_previewFitTargetOrigin;
            ApplyPreviewZoomTransform(next,p);
            if(next==_previewTargetZoom &&
               p.X==_previewFitTargetOrigin.X && p.Y==_previewFitTargetOrigin.Y)
            {
                _previewZoomTimer.Stop();
                _previewAnimatingFit=false;
            }
        }
        else
        {
            MovePreviewZoom(next);
            if(next==_previewTargetZoom)_previewZoomTimer.Stop();
        }
    }

    private void OnPreviewZoomFrame(object? sender,EventArgs e)
    {
        long now=Stopwatch.GetTimestamp();
        double seconds=Stopwatch.GetElapsedTime(_previewLastZoomTick,now).TotalSeconds;
        _previewLastZoomTick=now;
        double fraction=1-Math.Exp(-Math.Clamp(seconds,.001,.075)/.045);
        AdvancePreviewZoom(fraction);
    }

    private void OnPreviewZoom100(object? sender,RoutedEventArgs e) => StartPreviewPresetZoom(false);
    private void OnPreviewZoomFit(object? sender,RoutedEventArgs e) => StartPreviewPresetZoom(true);

    private void OnPreviewShortcutKeyDown(object? sender,KeyEventArgs e)
    {
        if(e.Handled || e.KeyModifiers!=KeyModifiers.Control)return;
        if(e.Key is Key.D1 or Key.NumPad1)
        {
            StartPreviewPresetZoom(false);
            e.Handled=true;
        }
        else if(e.Key is Key.D0 or Key.NumPad0)
        {
            StartPreviewPresetZoom(true);
            e.Handled=true;
        }
    }

    private void StartPreviewPresetZoom(bool fit)
    {
        var viewport=this.FindControl<Canvas>("PreviewViewport");
        if(viewport is null || viewport.Bounds.Width<=0 || viewport.Bounds.Height<=0)return;
        EnsurePreviewOrigin(viewport);
        _previewFitActive=fit;
        _previewAnimatingFit=fit;
        _previewZoomAnchor=new Point(viewport.Bounds.Width/2d,viewport.Bounds.Height/2d);
        double scale=fit?PreviewViewportGeometry.FitScale(
            viewport.Bounds.Width,viewport.Bounds.Height,
            _vm.PreviewDisplayWidth,_vm.PreviewDisplayHeight):1;
        _previewTargetZoom=scale;
        _vm.PreviewZoomPercent=scale*100d;
        if(fit)
            _previewFitTargetOrigin=PreviewViewportGeometry.CenteredOrigin(
                viewport.Bounds.Width,viewport.Bounds.Height,
                _vm.PreviewDisplayWidth,_vm.PreviewDisplayHeight,scale);
        // Begin with this UI event: no rendering, delayed positioning or re-layout.
        AdvancePreviewZoom(.24);
        if(_previewAnimatingFit || Math.Abs(_previewTargetZoom-_previewRenderedZoom)>1e-5)
        {
            _previewLastZoomTick=Stopwatch.GetTimestamp();
            _previewZoomTimer.Start();
        }
        else _previewZoomTimer.Stop();
    }

    private void EnsurePreviewOrigin(Canvas viewport)
    {
        if(_previewOriginInitialized || viewport.Bounds.Width<=0 || viewport.Bounds.Height<=0)return;
        double scale=_previewRenderedZoom;
        _previewOrigin=new Point(
            (viewport.Bounds.Width-_vm.PreviewDisplayWidth*scale)/2d,
            (viewport.Bounds.Height-_vm.PreviewDisplayHeight*scale)/2d);
        _previewOriginInitialized=true;
        ApplyPreviewOrigin();
    }

    private void ApplyPreviewOrigin()
    {
        // Pure render transform: no document relayout, even during drag and
        // the 16-ms zoom animation. Scale followed by translation is one
        // coherent transform in the compositor's coordinate system.
        if(_previewTranslate is null)return;
        _previewTranslate.X=_previewOrigin.X;
        _previewTranslate.Y=_previewOrigin.Y;
        RefreshPreviewScrollbars();
    }

    private void OnPreviewViewportSizeChanged(object? sender,SizeChangedEventArgs e)
    {
        if(sender is not Canvas viewport)return;
        if(!_previewOriginInitialized)
        {
            EnsurePreviewOrigin(viewport);
            return;
        }
        if(_previewFitActive)
        {
            StartPreviewPresetZoom(true);
            return;
        }
        // Keep the same document position at the centre while resizing a pane.
        _previewZoomAnchor=new Point(
            _previewZoomAnchor.X+(e.NewSize.Width-e.PreviousSize.Width)/2d,
            _previewZoomAnchor.Y+(e.NewSize.Height-e.PreviousSize.Height)/2d);
        _previewOrigin=new Point(
            _previewOrigin.X+(e.NewSize.Width-e.PreviousSize.Width)/2d,
            _previewOrigin.Y+(e.NewSize.Height-e.PreviousSize.Height)/2d);
        ApplyPreviewOrigin();
    }

    private void OnPreviewPageSizeChanged(object? sender,SizeChangedEventArgs e)
    {
        if(!_previewOriginInitialized || e.PreviousSize.Width<=0 ||
            e.PreviousSize.Height<=0 || e.NewSize.Width<=0 || e.NewSize.Height<=0)return;
        var viewport=this.FindControl<Canvas>("PreviewViewport");
        if(viewport is null)return;
        if(_previewFitActive)
        {
            StartPreviewPresetZoom(true);
            return;
        }
        // A change of paper/card geometry preserves the document point at the
        // viewport centre. Wheel zoom never triggers this: base size is fixed.
        var centre=new Point(viewport.Bounds.Width/2d,viewport.Bounds.Height/2d);
        _previewOrigin=new Point(
            centre.X-(centre.X-_previewOrigin.X)*e.NewSize.Width/e.PreviousSize.Width,
            centre.Y-(centre.Y-_previewOrigin.Y)*e.NewSize.Height/e.PreviousSize.Height);
        ApplyPreviewOrigin();
    }

    private void RefreshPreviewScrollbars()
    {
        if(!_previewOriginInitialized)return;
        var viewport=this.FindControl<Canvas>("PreviewViewport");
        var horizontal=this.FindControl<ScrollBar>("PreviewHorizontalScrollBar");
        var vertical=this.FindControl<ScrollBar>("PreviewVerticalScrollBar");
        if(viewport is null || horizontal is null || vertical is null)return;
        double scale=_previewRenderedZoom;
        const double margin=12;
        double vw=viewport.Bounds.Width,vh=viewport.Bounds.Height;
        double dw=_vm.PreviewDisplayWidth*scale,dh=_vm.PreviewDisplayHeight*scale;
        double mx=Math.Max(0,dw+2*margin-vw),my=Math.Max(0,dh+2*margin-vh);
        _updatingPreviewScrollbars=true;
        try
        {
            horizontal.Maximum=mx;
            horizontal.ViewportSize=Math.Max(1,vw);
            horizontal.LargeChange=Math.Max(1,vw*.8);
            horizontal.SmallChange=35;
            horizontal.IsEnabled=mx>0;
            horizontal.Value=Math.Clamp(margin-_previewOrigin.X,0,mx);
            vertical.Maximum=my;
            vertical.ViewportSize=Math.Max(1,vh);
            vertical.LargeChange=Math.Max(1,vh*.8);
            vertical.SmallChange=35;
            vertical.IsEnabled=my>0;
            vertical.Value=Math.Clamp(margin-_previewOrigin.Y,0,my);
        }
        finally { _updatingPreviewScrollbars=false; }
    }

    private void StopPreviewZoomAtCurrentFrame()
    {
        if(_previewZoomTimer.IsEnabled)
        {
            _previewZoomTimer.Stop();
            _previewTargetZoom=_previewRenderedZoom;
            _vm.PreviewZoomPercent=_previewRenderedZoom*100d;
        }
        _previewAnimatingFit=false;
    }

    private void OnPreviewHorizontalScroll(object? sender,RangeBaseValueChangedEventArgs e)
    {
        if(_updatingPreviewScrollbars || !_previewOriginInitialized)return;
        _previewFitActive=false;
        StopPreviewZoomAtCurrentFrame();
        _previewOrigin=new Point(12-e.NewValue,_previewOrigin.Y);
        ApplyPreviewOrigin();
    }

    private void OnPreviewVerticalScroll(object? sender,RangeBaseValueChangedEventArgs e)
    {
        if(_updatingPreviewScrollbars || !_previewOriginInitialized)return;
        _previewFitActive=false;
        StopPreviewZoomAtCurrentFrame();
        _previewOrigin=new Point(_previewOrigin.X,12-e.NewValue);
        ApplyPreviewOrigin();
    }

    private void OnPreviewPanPressed(object? sender,PointerPressedEventArgs e)
    {
        if(sender is not Grid page || !e.GetCurrentPoint(page).Properties.IsLeftButtonPressed)return;
        var viewport=this.FindControl<Canvas>("PreviewViewport");
        if(viewport is null)return;
        EnsurePreviewOrigin(viewport);
        _previewFitActive=false;
        // Dragging starts exactly where the animated zoom is now; never snap
        // to a still-pending target while the user grabs the page.
        StopPreviewZoomAtCurrentFrame();
        _previewPanning=true;
        _previewPanStart=e.GetPosition(viewport);
        _previewPanOrigin=new Vector(_previewOrigin.X,_previewOrigin.Y);
        e.Pointer.Capture(page);
        e.Handled=true;
    }

    private void OnPreviewPanMoved(object? sender,PointerEventArgs e)
    {
        if(!_previewPanning)return;
        var viewport=this.FindControl<Canvas>("PreviewViewport");
        if(viewport is null)return;
        var point=e.GetPosition(viewport);
        _previewOrigin=new Point(
            _previewPanOrigin.X+point.X-_previewPanStart.X,
            _previewPanOrigin.Y+point.Y-_previewPanStart.Y);
        ApplyPreviewOrigin();
        e.Handled=true;
    }

    private void OnPreviewPanReleased(object? sender,PointerReleasedEventArgs e)
    {
        if(!_previewPanning)return;
        _previewPanning=false;
        e.Pointer.Capture(null);
        e.Handled=true;
    }

    private void OnPreviewPanCaptureLost(object? sender,PointerCaptureLostEventArgs e)
    {
        _previewPanning=false;
    }

    private void OnExportPdf(object? sender,RoutedEventArgs e) => _ = ExportPrintDocumentAsync(false);
    private void OnExportRaster(object? sender,RoutedEventArgs e) => _ = ExportPrintDocumentAsync(true);

    private async Task ExportPrintDocumentAsync(bool raster)
    {
        var snapshot=_vm.SnapshotPrintableJob();
        if(snapshot is null)return;
        long vaultGeneration=_vaultGeneration;
        string extension=raster?"zip":"pdf";
        var picker=new FilePickerSaveOptions
        {
            Title=UiText.Tr(raster?"Экспорт листов 600 DPI":"Экспорт векторного документа PDF"),
            SuggestedFileName="KeePassXCCardPrint-Print."+extension,
            DefaultExtension=extension,
            FileTypeChoices=[new FilePickerFileType(raster?"PNG pages in ZIP":"Vector PDF")
            {
                Patterns=raster?new[] { "*.zip" }:new[] { "*.pdf" }
            }]
        };
        var file=await StorageProvider.SaveFilePickerAsync(picker);
        if(file is null || _vaultGeneration!=vaultGeneration)return;
        // File picker chooses the destination; no sensitive bytes are created or
        // written until this explicit, always-required red confirmation.
        if(!await AppConfirmations.ConfirmSecretFileAsync(this,_settings.Language,false))return;
        if(_vaultGeneration!=vaultGeneration)return;
        _printExportCancellation?.Cancel();
        var source=new CancellationTokenSource();
        _printExportCancellation=source;
        var token=source.Token;
        _vm.SetExporting(true);
        byte[]? data=null;
        try
        {
            // Nothing expensive runs on Avalonia's UI dispatcher.
            var (appearance,sheets)=snapshot.Value;
            data=await Task.Run(() => raster
                ? Rendering.PrintDocumentPipeline.ExportRasterZip(sheets,appearance,token)
                : Rendering.PrintDocumentPipeline.ExportVectorPdf(sheets,appearance,token),token);
            token.ThrowIfCancellationRequested();
            using var destination=await file.OpenWriteAsync();
            await destination.WriteAsync(data,token);
            await destination.FlushAsync(token);
            _vm.SetPreviewNotice("Печатный документ сохранён. Секреты в экспортированном файле не зашифрованы.");
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested)
        {
            if(_vaultGeneration==vaultGeneration)
                _vm.SetPreviewNotice("Экспорт отменён.");
        }
        catch(Exception)
        {
            // Do not put exception messages, filenames, credentials or content into logs.
            if(_vaultGeneration==vaultGeneration)
                _vm.SetPreviewNotice("Не удалось экспортировать документ. Проверьте место на диске и права доступа.");
        }
        finally
        {
            if(data is not null)Array.Clear(data);
            if(ReferenceEquals(_printExportCancellation,source))_printExportCancellation=null;
            source.Dispose();
            _vm.SetExporting(false);
        }
    }

    private void OnRecordMarkPrinted(object? sender,RoutedEventArgs e)
    {
        if((sender as MenuItem)?.CommandParameter is VaultRecord entry)
            _vm.MarkPrintStatus(entry,"printed");
    }
    private void OnRecordMarkUnprinted(object? sender,RoutedEventArgs e)
    {
        if((sender as MenuItem)?.CommandParameter is VaultRecord entry)
            _vm.MarkPrintStatus(entry,"unprinted");
    }
    private void OnRecordExcludeTracking(object? sender,RoutedEventArgs e)
    {
        if((sender as MenuItem)?.CommandParameter is VaultRecord entry)
            _vm.MarkPrintStatus(entry,"exclude");
    }
    private async void OnOpenPrintDialog(object? sender,RoutedEventArgs e)
    {
        var job=_vm.SnapshotPrintableJob();
        if(job is null)return;
        await new PrintDialog(_vm,_settings,job.Value,Persist).ShowDialog(this);
    }
    private async void OnOpenPrintHistory(object? sender,RoutedEventArgs e)
        => await new PrintHistoryWindow(_vm).ShowDialog(this);

    private void OnPreviewToggleToolbarDock(object? sender, RoutedEventArgs e)
    {
        _settings.PreviewToolbarDock = _settings.PreviewToolbarDock == "side" ? "top" : "side";
        ApplyPreviewToolbarDock();
        Persist();
    }

    // Move the ONE toolbar rather than maintaining two copies of interactive
    // controls. Dock changes do not reset the card selection or viewport zoom.
    private void ApplyPreviewToolbarDock()
    {
        var top = this.FindControl<Border>("PreviewToolbarTopHost");
        var side = this.FindControl<ScrollViewer>("PreviewToolbarSideHost");
        var toolbar = this.FindControl<StackPanel>("PreviewToolbar");
        if (top is null || side is null || toolbar is null) return;
        double width = this.FindControl<Grid>("PreviewPane")?.Bounds.Width ?? 0;
        bool dockedSide = AdaptiveViewport.UseSideToolbar(_settings.PreviewToolbarDock, width, _vm.PreviewDuplexAvailable);
        if (dockedSide && !ReferenceEquals(side.Content, toolbar))
        {
            top.Child = null;
            side.Content = toolbar;
        }
        else if (!dockedSide && !ReferenceEquals(top.Child, toolbar))
        {
            side.Content = null;
            top.Child = toolbar;
        }
        toolbar.Orientation = dockedSide ? Avalonia.Layout.Orientation.Vertical : Avalonia.Layout.Orientation.Horizontal;
        // The same command groups follow the toolbar in either docking position.
        foreach (var child in toolbar.Children)
            if (child is StackPanel group) group.Orientation = toolbar.Orientation;
        toolbar.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        toolbar.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        side.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        top.IsVisible = !dockedSide;
        side.IsVisible = dockedSide;
        var dockButton = this.FindControl<Button>("PreviewToolbarDockButton");
        if (dockButton is not null)
            ToolTip.SetTip(dockButton, UiText.Tr(_settings.PreviewToolbarDock == "side"
                ? "Размещать панель сверху при достаточной ширине"
                : "Закрепить панель вертикально слева"));
    }

    private void OnPreviewPrevious(object? sender, RoutedEventArgs e) => _vm.PreviewPrevious();
    private void OnPreviewNext(object? sender, RoutedEventArgs e) => _vm.PreviewNext();
    private void OnPreviewFront(object? sender, RoutedEventArgs e) => _vm.PreviewFront();
    private void OnPreviewBack(object? sender, RoutedEventArgs e) => _vm.PreviewBack();
    private void OnSaveAppearance(object? sender, RoutedEventArgs e) => _vm.SaveChangesToTemplate();
    // Both template selectors refer to the same preview model. A selection is
    // a load request; do not process the echo from the other selector.
    private bool _handlingTemplateSelection;

    private async void OnCardTemplateSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_handlingTemplateSelection || sender is not ComboBox { SelectedItem: string selection } ||
            _vm is null) return;
        var vm = _vm;
        _handlingTemplateSelection = true;
        try
        {
            if (string.Equals(selection, vm.ActiveTemplateSelection, StringComparison.Ordinal)) return;
            if (vm.HasUnsavedTemplateChanges)
            {
                var language = Localizer.ResolveLanguage(vm.Settings.Language);
                var accepted = await AppConfirmations.AskAsync(this,
                    language switch
                    {
                        "uk" => "Незбережені зміни шаблона",
                        "en" => "Unsaved template changes",
                        _ => "Несохранённые изменения шаблона"
                    },
                    language switch
                    {
                        "uk" => "Поточний шаблон має незбережені зміни. Відкинути їх і завантажити вибраний шаблон?",
                        "en" => "The current template has unsaved changes. Discard them and load the selected template?",
                        _ => "В текущем шаблоне есть несохранённые изменения. Отменить их и загрузить выбранный шаблон?"
                    },
                    language switch
                    {
                        "uk" => "Завантажити", "en" => "Load", _ => "Загрузить"
                    },
                    sensitive: true, actionIcon: "warning",
                    cancelText: language switch
                    {
                        "uk" => "Скасувати", "en" => "Cancel", _ => "Отмена"
                    });
                if (!accepted)
                {
                    if (ReferenceEquals(_vm, vm)) vm.SelectedTemplate = vm.ActiveTemplateSelection;
                    return;
                }
            }
            if (!ReferenceEquals(_vm, vm)) return;
            // Explicitly synchronize the chosen value before the load: Avalonia
            // may deliver SelectionChanged before a TwoWay binding updates.
            vm.SelectedTemplate = selection;
            if (!vm.LoadCardTemplate(discardUnsavedChanges: true))
                vm.SelectedTemplate = vm.ActiveTemplateSelection;
        }
        catch (Exception ex)
        {
            try { CloudSyncDiagnostics.Record("template-selection", ex); }
            catch { /* Diagnostics must not crash the Avalonia dispatcher. */ }
            try
            {
                if (ReferenceEquals(_vm, vm)) vm.SelectedTemplate = vm.ActiveTemplateSelection;
            }
            catch { /* Keep the dispatcher alive if the view model was disposed. */ }
        }
        finally { _handlingTemplateSelection = false; }
    }

    private async void OnSaveCardTemplate(object? sender, RoutedEventArgs e)
    {
        try { await ShowCreateTemplateDialogAsync(); }
        catch (Exception ex)
        {
            try { CloudSyncDiagnostics.Record("create-template-dialog", ex); }
            catch { /* A logging failure must not crash the Avalonia dispatcher. */ }
        }
    }

    private async Task ShowCreateTemplateDialogAsync()
    {
        var vm = _vm;
        var language = Localizer.ResolveLanguage(_settings.Language);
        var caption = language switch
        {
            "uk" => "Створити шаблон",
            "en" => "Create template",
            _ => "Создать шаблон"
        };
        var dialog = new Window
        {
            Title = "KeePassXC Card Print — " + caption,
            Width = 440,
            MinWidth = 370,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Background
        };
        PrintWindowIcons.Attach(dialog);
        var nameBox = new TextBox
        {
            Text = vm.TemplateName,
            PlaceholderText = vm.UiTemplateNamePlaceholder
        };
        var feedback = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        var createText = language switch
        {
            "uk" => "Створити", "en" => "Create", _ => "Создать"
        };
        var cancelText = language switch
        {
            "uk" => "Скасувати", "en" => "Cancel", _ => "Отмена"
        };
        var create = new Button { Content = createText, MinWidth = 94 };
        var cancel = new Button { Content = cancelText, MinWidth = 94 };
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        buttons.Children.Add(create);
        buttons.Children.Add(cancel);
        var content = new StackPanel { Spacing = 9 };
        content.Children.Add(new TextBlock { Text = caption, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        content.Children.Add(nameBox);
        content.Children.Add(feedback);
        content.Children.Add(buttons);
        var card = new Border { Margin = new Thickness(12), Padding = new Thickness(12), Child = content };
        card.Classes.Add("settings-card");
        dialog.Content = card;
        create.Click += (_, _) =>
        {
            var name = nameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                feedback.Text = language switch
                {
                    "uk" => "Вкажіть назву шаблону.",
                    "en" => "Enter a template name.",
                    _ => "Укажите название шаблона."
                };
                feedback.IsVisible = true;
                return;
            }
            vm.TemplateName = name;
            vm.SaveCardTemplate();
            if (vm.HasDesignMessage)
            {
                feedback.Text = vm.DesignMessage;
                feedback.IsVisible = true;
                return;
            }
            dialog.Close(true); // SaveCardTemplate already activates and selects the new template.
        };
        cancel.Click += (_, _) => dialog.Close(false);
        dialog.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { args.Handled = true; dialog.Close(false); }
        };
        dialog.Opened += (_, _) => { nameBox.Focus(); nameBox.SelectAll(); };
        await dialog.ShowDialog<bool>(this);
    }

    private async void OnRenameCardTemplate(object? sender, RoutedEventArgs e)
    {
        var vm = _vm;
        if (!vm.CanManagePersonalTemplate) return;
        var language = Localizer.ResolveLanguage(_settings.Language);
        var title = language switch
        {
            "uk" => "Перейменувати шаблон", "en" => "Rename template", _ => "Переименовать шаблон"
        };
        var renameLabel = language switch
        {
            "uk" => "Перейменувати", "en" => "Rename", _ => "Переименовать"
        };
        var cancelLabel = language switch
        {
            "uk" => "Скасувати", "en" => "Cancel", _ => "Отмена"
        };
        var dialog = new Window
        {
            Title = "KeePassXC Card Print — " + title,
            Width = 440, MinWidth = 370, SizeToContent = SizeToContent.Height,
            CanResize = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Background
        };
        PrintWindowIcons.Attach(dialog);
        var input = new TextBox { Text = vm.ActivePersonalTemplateName, MaxLength = 64 };
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var rename = PrintDialogButtons.Create(renameLabel, "settings");
        var cancel = PrintDialogButtons.Create(cancelLabel, "cancel");
        var actions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        actions.Children.Add(rename);
        actions.Children.Add(cancel);
        var contents = new StackPanel { Spacing = 9 };
        contents.Children.Add(new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        contents.Children.Add(input);
        contents.Children.Add(feedback);
        contents.Children.Add(actions);
        var card = new Border { Margin = new Thickness(12), Padding = new Thickness(12), Child = contents };
        card.Classes.Add("settings-card");
        dialog.Content = card;
        rename.Click += (_, _) =>
        {
            if (vm.RenameCurrentPersonalTemplate(input.Text ?? "")) dialog.Close(true);
            else { feedback.Text = vm.DesignMessage; feedback.IsVisible = true; }
        };
        cancel.Click += (_, _) => dialog.Close(false);
        dialog.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { args.Handled = true; dialog.Close(false); }
        };
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        await dialog.ShowDialog<bool>(this);
    }

    private async void OnDeleteCardTemplate(object? sender, RoutedEventArgs e)
    {
        var vm = _vm;
        if (!vm.CanManagePersonalTemplate) return;
        var language = Localizer.ResolveLanguage(_settings.Language);
        var title = language switch
        {
            "uk" => "Видалити шаблон?", "en" => "Delete template?", _ => "Удалить шаблон?"
        };
        var description = language switch
        {
            "uk" => $"Шаблон «{vm.ActivePersonalTemplateName}» буде видалено без можливості скасування. Незбережені зміни буде втрачено. Продовжити?",
            "en" => $"Template “{vm.ActivePersonalTemplateName}” will be permanently deleted. Unsaved changes will be lost. Continue?",
            _ => $"Шаблон «{vm.ActivePersonalTemplateName}» будет удалён без возможности отмены. Несохранённые изменения пропадут. Продолжить?"
        };
        var delete = language switch { "uk" => "Видалити", "en" => "Delete", _ => "Удалить" };
        var cancel = language switch { "uk" => "Скасувати", "en" => "Cancel", _ => "Отмена" };
        if (await AppConfirmations.AskAsync(this, title, description, delete,
                sensitive: true, actionIcon: "warning", cancelText: cancel) && ReferenceEquals(_vm, vm))
            vm.DeleteCurrentPersonalTemplate();
    }

    private async void OnResetMapping(object? sender, RoutedEventArgs e)
    {
        if(await AppConfirmations.AskAsync(this,"Сбросить маппинг?",
            "Текущие настройки соответствия полей будут заменены значениями по умолчанию.",
            "Сбросить",false,"reset")) _vm.ResetMapping();
    }
    private async void OnEditDomains(object? sender, RoutedEventArgs e)
    {
        var dialog = new DomainsWindow(_vm.Settings.Language);
        await dialog.ShowDialog(this);
    }

}


