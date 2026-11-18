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
    public void SelectStorageFromSettings(OptionItem option) => StorageCombo.SelectedItem = option;
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
        _cloudSync.BeforeRemoteApplyAsync = async () => await Dispatcher.UIThread.InvokeAsync(() =>
            !_vm.Busy && !_vm.MappingHasPendingChanges && !_vm.CanSaveTemplateChanges &&
            _printExportCancellation is null);
        _cloudSync.RemoteApplied = () => Dispatcher.UIThread.Post(OnCloudRemoteApplied);
        Closed += (_, _) => _cloudSync.Dispose();
        Title = "KeePassXC Card Print  " + AppBuildInfo.Version;
        DataContext = _vm;
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

    // The outer viewport owns vertical overflow for the four editor tabs.
    // Keep their star-sized grids bounded: otherwise a Grid measured with infinite
    // height by ScrollViewer expands its tables/editors and loses inner scrolling.
    private void OnEditorTabScrollSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll || scroll.Content is not Grid page ||
            e.NewSize.Height <= 0) return;

        // Minimums leave usable space for both top controls and bottom content.
        // On larger windows the page fills the viewport without spare blank space.
        double minimum = scroll.Name switch
        {
            "SourceTabScroll" => 560,
            "MappingTabScroll" => 550,
            "DesignTabScroll" => 430,
            "DuplexTabScroll" => 490,
            _ => 0
        };
        double available = Math.Max(0, e.NewSize.Height - page.Margin.Top - page.Margin.Bottom);
        double height = Math.Max(available, minimum);
        if (double.IsNaN(page.Height) || Math.Abs(page.Height - height) >= 0.5)
            page.Height = height;
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
        if (_vm.ActiveTab != 4 || !IsVisible) return;
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

    // Mapping layout is content-first: the editor is allowed to grow until it
    // shows all controls without an inner scrollbar. Remaining vertical space
    // belongs to the table, which keeps a usable minimum height. On shorter
    // windows only the editor becomes scrollable.
    private void OnMappingTabSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not Grid grid || e.NewSize.Height <= 0) return;

        Border? editor = null;
        double reservedHeight = grid.RowSpacing * 4;
        foreach (var child in grid.Children)
        {
            var row = Grid.GetRow(child);
            if (row == 4 && child is Border panel)
                editor = panel;
            else if (row is 0 or 1 or 2)
                reservedHeight += child.Bounds.Height;
        }
        if (editor is null) return;

        // During the first measure, child Bounds can still be zero. Use a
        // conservative allowance until the controls have been arranged.
        if (reservedHeight < 65) reservedHeight = 105;
        const double minTableHeight = 170;
        const double minEditorHeight = 70;
        var availableForEditor = Math.Max(minEditorHeight,
            Math.Floor(e.NewSize.Height - reservedHeight - minTableHeight));
        if (Math.Abs(editor.MaxHeight - availableForEditor) >= 1)
            editor.MaxHeight = availableForEditor;
    }

    private async void OnStorageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_switchingStorage || sender is not ComboBox combo ||
            combo.SelectedItem is not OptionItem selected || _vm is null) return;
        var target = selected.Code switch
        {
            "local" => ConfigurationStorage.Local,
            "cloud" => ConfigurationStorage.Cloud,
            _ => ConfigurationStorage.AppData
        };
        if (target == SettingsLocation.Mode) return;
        _switchingStorage = true;
        try
        {
            if (_vm.Busy)
            {
                _vm.VaultStatus = "Дождитесь завершения открытия базы.";
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
                _vm.ActiveTab = 4;
                await AppConfirmations.AskAsync(this,
                    lang switch { "uk" => "Хмарне зберігання", "en" => "Cloud storage", _ => "Облачное хранение" },
                    message, lang switch { "uk" => "Зрозуміло", "en" => "OK", _ => "Понятно" },
                    cancelText: lang switch { "uk" => "Закрити", "en" => "Close", _ => "Закрыть" });
                return;
            }
            var answer = await new StorageSwitchDialog(_settings.Language, target)
                .ShowDialog<StorageSwitchAnswer>(this);
            if (answer == StorageSwitchAnswer.Cancel) return;

            // The source configuration remains available for later switching back.
            if (!await ResolveUnsavedMappingAsync()) return;
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
            if (target == ConfigurationStorage.Cloud) _cloudSync.Start();
            else _cloudSync.Stop();
            // Reload ALL preferences from the selected folder. This includes UI
            // theme/language, mapping, YubiKey choices and selection tokens.
            _vm.PreferencesChanged -= PreferencesChanged;
            _vm.ClearVault();
            _store = new UiSettingsStore();
            _settings = _store.Load();
            _vm = new ShellViewModel(_settings);
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
        }
        catch (Exception)
        {
            // Never disclose filesystem details or a copied configuration's contents.
            _vm.VaultStatus = "[STORAGE] Не удалось переключить хранение. Проверьте права записи и свободное место.";
        }
        finally
        {
            // Reverting the visual choice must never trigger a second dialog.
            combo.SelectedItem = _vm.SelectedStorage;
            _switchingStorage = false;
        }
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
        // Client-area mark reacts to theme and uses an unscaled ICO frame at the current DPI.
        // The OS-level window/taskbar icon always stays white, regardless of theme.
        var logo = this.FindControl<Image>("AppHeaderIcon");
        if (logo is not null)
            BrandArtwork.Apply(logo, ActualThemeVariant == ThemeVariant.Dark, RenderScaling, 40);
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
    internal string UpdateLanguage => _settings.Language;
    internal bool IsExportRunning => _printExportCancellation is not null;
    internal Task<bool> ReadyForUpdateAsync() => ResolveUnsavedMappingAsync(exiting:true);
    internal void CloseForUpdate() { _exitApproved=true; Close(); }
    private async Task<bool> ResolveUnsavedMappingAsync(bool exiting=false)
    {
        if(!_vm.MappingHasPendingChanges)return true;
        var decision=await new UnsavedMappingDialog(_settings.Language,exiting)
            .ShowDialog<UnsavedMappingChoice>(this);
        if(decision==UnsavedMappingChoice.Discard)return true;
        if(decision!=UnsavedMappingChoice.Save)return false;
        if(_vm.SaveMapping())return true;
        _vm.ActiveTab=1; // Leave validation failures visible.
        return false;
    }
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if(!_exitApproved)
        {
            e.Cancel=true;
            if(_exitPromptOpen)return;
            _exitPromptOpen=true;
            try
            {
                // Dirty mapping has its own Save / Don't save / Cancel dialog.
                // Never show a second ordinary exit confirmation afterwards.
                bool approved=_vm.MappingHasPendingChanges
                    ? await ResolveUnsavedMappingAsync(exiting:true)
                    : await AppConfirmations.ConfirmExitAsync(this,_settings.Language,
                        _printExportCancellation is not null);
                if(approved)
                {
                    _exitApproved=true;
                    Close();
                }
            }
            finally{_exitPromptOpen=false;}
            return;
        }
        _passwordLayout.Leave();
        _vaultGeneration++;
        _printExportCancellation?.Cancel();
        _vm.SaveVaultPreferences();
        _vm.SaveCardAppearance();
        _vm.ClearVault();
        CaptureLayout();
        Persist();
        if (SettingsLocation.Mode == ConfigurationStorage.Cloud) _cloudSync.MarkPending();
        UpdaterHost.InstallOnExitIfReady();
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
        bool dockedSide = _settings.PreviewToolbarDock == "side";
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
        toolbar.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        toolbar.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        side.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        top.IsVisible = !dockedSide;
        side.IsVisible = dockedSide;
        var dockButton = this.FindControl<Button>("PreviewToolbarDockButton");
        if (dockButton is not null)
            ToolTip.SetTip(dockButton, dockedSide ? "Разместить панель сверху" : "Разместить панель вертикально слева");
    }

    private void OnPreviewPrevious(object? sender, RoutedEventArgs e) => _vm.PreviewPrevious();
    private void OnPreviewNext(object? sender, RoutedEventArgs e) => _vm.PreviewNext();
    private void OnPreviewFront(object? sender, RoutedEventArgs e) => _vm.PreviewFront();
    private void OnPreviewBack(object? sender, RoutedEventArgs e) => _vm.PreviewBack();
    private void OnSaveAppearance(object? sender, RoutedEventArgs e) => _vm.SaveChangesToTemplate();
    private void OnLoadCardTemplate(object? sender, RoutedEventArgs e) => _vm.LoadCardTemplate();
    private void OnSaveCardTemplate(object? sender, RoutedEventArgs e) => _vm.SaveCardTemplate();
    private async void OnResetMapping(object? sender, RoutedEventArgs e)
    {
        if(await AppConfirmations.AskAsync(this,"Сбросить маппинг?",
            "Текущие настройки соответствия полей будут заменены значениями по умолчанию.",
            "Сбросить",false,"reset")) _vm.ResetMapping();
    }
    private void OnSaveMapping(object? sender, RoutedEventArgs e) => _vm.SaveMapping();
    private async void OnEditDomains(object? sender, RoutedEventArgs e)
    {
        var dialog = new DomainsWindow(_vm.Settings.Language);
        await dialog.ShowDialog(this);
    }

}


