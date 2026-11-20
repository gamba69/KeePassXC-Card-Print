using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.Platforms;

namespace KeePassXCCardPrint.ViewModels;

public sealed class OptionItem : INotifyPropertyChanged
{
    private string _label = "";
    public string Code { get; }
    public string Label
    {
        get => _label;
        private set { _label = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public OptionItem(string code, string labelKey, string language)
    {
        Code = code;
        _labelKey = labelKey;
        Relocalize(language);
    }
    private readonly string _labelKey;
    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }
    public void Relocalize(string language) => Label = Localizer.Text(language, _labelKey);
}

// Unified shell and vault state; no sensitive values are persisted.
public sealed partial class ShellViewModel : INotifyPropertyChanged
{
    private readonly UiSettings _settings;
    private OptionItem _selectedTheme;
    private OptionItem _selectedLanguage;
    private OptionItem _selectedStorage;
    private int _activeTab;
    public AboutViewModel About { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? PreferencesChanged;

    public ObservableCollection<OptionItem> ThemeOptions { get; } = new();
    public ObservableCollection<OptionItem> LanguageOptions { get; } = new();
    public ObservableCollection<OptionItem> StorageOptions { get; } = new();
    public OptionItem SelectedStorage => _selectedStorage;
    public void SetStorageMode(ConfigurationStorage mode)
    {
        _selectedStorage = StorageOptions.First(o => o.Code ==
            (mode == ConfigurationStorage.Local ? "local" : mode == ConfigurationStorage.Cloud ? "cloud" : "appdata"));
        Changed(nameof(SelectedStorage));
    }

    public ShellViewModel(UiSettings settings)
    {
        _settings = settings;
        About = new AboutViewModel(settings);
        foreach (var (code, key) in new [] { ("auto", "AutoTheme"), ("light", "Light"), ("dark", "Dark") })
            ThemeOptions.Add(new OptionItem(code, key, settings.Language));
        foreach (var (code, key) in new [] { ("auto", "AutoLanguage"), ("ru", "Russian"), ("uk", "Ukrainian"), ("en", "English") })
            LanguageOptions.Add(new OptionItem(code, key, settings.Language));
        foreach (var (code, key) in new []
            { ("cloud", "StorageCloud"), ("appdata", "StorageAppData"), ("local", "StorageLocal") })
            StorageOptions.Add(new OptionItem(code, key, settings.Language));
        _selectedStorage = StorageOptions.First(o => o.Code ==
            (SettingsLocation.Mode == ConfigurationStorage.Local ? "local" :
             SettingsLocation.Mode == ConfigurationStorage.Cloud ? "cloud" : "appdata"));
        _selectedTheme = ThemeOptions.First(o => o.Code == settings.Theme);
        _selectedLanguage = LanguageOptions.First(o => o.Code == settings.Language);
        _activeTab = settings.ActiveTab;
        InitializeVault();
        InitializeCardPreview();
    }

    public UiSettings Settings => _settings;
    public OptionItem SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is null || ReferenceEquals(_selectedTheme, value)) return;
            _selectedTheme = value;
            _settings.Theme = value.Code;
            Changed();
            PreferencesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public OptionItem SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || ReferenceEquals(_selectedLanguage, value)) return;
            _selectedLanguage = value;
            _settings.Language = value.Code;
            foreach (var item in ThemeOptions) item.Relocalize(value.Code);
            foreach (var item in LanguageOptions) item.Relocalize(value.Code);
            foreach (var item in StorageOptions) item.Relocalize(value.Code);
            Changed();
            // Explicit notifications keep compiled bindings predictable under AOT.
            foreach (var property in LocalizedPropertyNames)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
            RelocalizeMapping();
            About.Relocalize();
            RelocalizeAppearance();
            RefreshPrintStatuses();
            Changed(nameof(DesignMessage));
            Changed(nameof(PreviewError));
            Changed(nameof(PreviewFaceLabel));
            Changed(nameof(PreviewAnimationDurationCaption));
            PreferencesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    // In a splitter-minimized pane the four primary tabs become icon-only;
    // the right-anchored About tab retains its caption and all tabs have tooltips.
    private bool _showMainTabCaptions = true;
    public bool ShowMainTabCaptions
    {
        get => _showMainTabCaptions;
        set
        {
            if (_showMainTabCaptions == value) return;
            _showMainTabCaptions = value;
            Changed();
        }
    }

    public int ActiveTab
    {
        get => _activeTab;
        set
        {
            if (value == _activeTab || value is < 0 or > 5) return;
            _activeTab = value;
            _settings.ActiveTab = value;
            Changed();
            PreferencesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Changed([CallerMemberName] string? property = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        if (property == nameof(Status))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLead)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusTail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLoadedStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowOperationalStatus)));
        }
    }

    private static readonly string[] LocalizedPropertyNames =
    [
        nameof(Interface),
        nameof(AppTagline),
        nameof(Theme),
        nameof(Language),
        nameof(Storage),
        nameof(YubiEnabledCaption),
        nameof(YubiSlotCaption),
        nameof(YubiShortCaption),
        nameof(YubiKeyHelp),
        nameof(YubiDeviceHelp),
        nameof(YubiRefreshHelp),
        nameof(YubiShortHelp),
        nameof(TabSource),
        nameof(TabMapping),
        nameof(TabDesign),
        nameof(TabDuplex),
        nameof(TabSettings),
        nameof(TabAbout),
        nameof(SourceTitle),
        nameof(Database),
        nameof(KeyFile),
        nameof(Password),
        nameof(Show),
        nameof(PasswordLayoutHelp),
        nameof(Open),
        nameof(Demo),
        nameof(DemoHelp),
        nameof(DemoDatabasePlaceholder),
        nameof(DemoKeyFilePlaceholder),
        nameof(Clear),
        nameof(RememberPathsCaption),
        nameof(RememberSelectionCaption),
        nameof(Forget),
        nameof(Records),
        nameof(Filter),
        nameof(FilterSelectedTip),
        nameof(FilterRemainingTip),
        nameof(FilterReprintTip),
        nameof(RefreshRecordsTip),
        nameof(ResetRecordFiltersTip),
        nameof(Group),
        nameof(Title),
        nameof(Print),
        nameof(SelectAll),
        nameof(SelectNone),
        nameof(SelectToPrint),
        nameof(SelectedZero),
        nameof(SelectedCountText),
        nameof(Icons),
        nameof(Apply),
        nameof(ReadOnlyNotice),
        nameof(MappingTitle),
        nameof(MappingHint),
        nameof(Scope),
        nameof(AllColumns),
        nameof(Position),
        nameof(Field),
        nameof(Section),
        nameof(Priority),
        nameof(OutputGroup),
        nameof(Format),
        nameof(Visibility),
        nameof(Label),
        nameof(LongValue),
        nameof(Grouping),
        nameof(FieldOptions),
        nameof(NotSelected),
        nameof(DesignTitle),
        nameof(Templates),
        nameof(Layout),
        nameof(CardsPerRow),
        nameof(CardWidth),
        nameof(CardHeight),
        nameof(ValueFont),
        nameof(TitleFont),
        nameof(FontSize),
        nameof(Colors),
        nameof(Background),
        nameof(TextColor),
        nameof(Rules),
        nameof(QR),
        nameof(QRPrint),
        nameof(Notes),
        nameof(Cutting),
        nameof(DuplexTitle),
        nameof(DuplexDescription),
        nameof(DuplexEnabled),
        nameof(DuplexContinuation),
        nameof(DuplexCombine),
        nameof(DuplexBack),
        nameof(DuplexFlip),
        nameof(TestAlignment),
        nameof(Calibration),
        nameof(Preview),
        nameof(Mode),
        nameof(Page),
        nameof(Side),
        nameof(Front),
        nameof(Back),
        nameof(Mono),
        nameof(Zoom),
        nameof(PreviewPlaceholder),
        nameof(PreviewUpdateLabel), nameof(PreviewNoCardsLabel),
        nameof(PreviewWarning),
        nameof(Printer),
        nameof(PrintFront),
        nameof(PrintBack),
        nameof(Paper),
        nameof(Status),
        nameof(StatusLead),
        nameof(StatusTail),
        nameof(UiTemplate),
        nameof(UiLoadTemplate),
        nameof(UiTemplateNamePlaceholder),
        nameof(UiCreateTemplate),
        nameof(UiSaveChanges),
        nameof(UiEmptyReverse),
        nameof(UiShowHeader),
        nameof(UiShowFooter),
        nameof(UiPreview),
        nameof(UiPreviewAndSheet),
        nameof(UiShowFullSheet),
        nameof(UiLandscape),
        nameof(UiShortEdgeFlip),
        nameof(UiFirstSlot),
        nameof(UiWheelStep),
        nameof(UiPreviewAnimation),
        nameof(UiTransitions),
        nameof(UiDuration),
        nameof(UiAfterRebuild),
        nameof(UiPrintStatus),
        nameof(UiMonochromePreview),
        nameof(UiNavigationAnimation),
        nameof(UiSideChangeAnimation),
    ];

    // View-model titles are all compiled-bound and can be relocalized without recreating controls.
    public string UiTemplate => Localizer.Text(_settings.Language, "UiTemplate");
    public string UiLoadTemplate => Localizer.Text(_settings.Language, "UiLoadTemplate");
    public string UiTemplateNamePlaceholder => Localizer.Text(_settings.Language, "UiTemplateNamePlaceholder");
    public string UiCreateTemplate => Localizer.Text(_settings.Language, "UiCreateTemplate");
    public string UiSaveChanges => Localizer.Text(_settings.Language, "UiSaveChanges");
    public string UiEmptyReverse => Localizer.Text(_settings.Language, "UiEmptyReverse");
    public string UiShowHeader => Localizer.Text(_settings.Language, "UiShowHeader");
    public string UiShowFooter => Localizer.Text(_settings.Language, "UiShowFooter");
    public string UiPreview => Localizer.Text(_settings.Language, "UiPreview");
    public string UiPreviewAndSheet => Localizer.Text(_settings.Language, "UiPreviewAndSheet");
    public string UiShowFullSheet => Localizer.Text(_settings.Language, "UiShowFullSheet");
    public string UiLandscape => Localizer.Text(_settings.Language, "UiLandscape");
    public string UiShortEdgeFlip => Localizer.Text(_settings.Language, "UiShortEdgeFlip");
    public string UiFirstSlot => Localizer.Text(_settings.Language, "UiFirstSlot");
    public string UiWheelStep => Localizer.Text(_settings.Language, "UiWheelStep");
    public string UiPreviewAnimation => Localizer.Text(_settings.Language, "UiPreviewAnimation");
    public string UiTransitions => Localizer.Text(_settings.Language, "UiTransitions");
    public string UiDuration => Localizer.Text(_settings.Language, "UiDuration");
    public string UiAfterRebuild => Localizer.Text(_settings.Language, "UiAfterRebuild");
    public string UiPrintStatus => Localizer.Text(_settings.Language, "UiPrintStatus");
    public string UiMonochromePreview => Localizer.Text(_settings.Language, "UiMonochromePreview");
    public string UiNavigationAnimation => Localizer.Text(_settings.Language, "UiNavigationAnimation");
    public string UiSideChangeAnimation => Localizer.Text(_settings.Language, "UiSideChangeAnimation");
    public string AppDisplayName => "KeePassXC Card Print";
    public string AppTagline => Localizer.Text(_settings.Language, "AppTagline");
    public string AppVersionPlatform => $"{AppBuildInfo.Version} · {PlatformServices.Current.PlatformLabel}";

    public string Interface => Localizer.Text(_settings.Language, "Interface");
    public string Theme => Localizer.Text(_settings.Language, "Theme");
    public string Language => Localizer.Text(_settings.Language, "Language");
    public string Storage => Localizer.Text(_settings.Language, "Storage");
    public string TabSource => Localizer.Text(_settings.Language, "TabSource");
    public string TabMapping => Localizer.Text(_settings.Language, "TabMapping");
    public string TabDesign => Localizer.Text(_settings.Language, "TabDesign");
    public string TabDuplex => Localizer.Text(_settings.Language, "TabDuplex");
    public string TabSettings => Localizer.Text(_settings.Language, "TabSettings");
    public string TabAbout => Localizer.Text(_settings.Language, "TabAbout");
    public string SourceTitle => Localizer.Text(_settings.Language, "SourceTitle");
    public string Database => Localizer.Text(_settings.Language, "Database");
    public string KeyFile => Localizer.Text(_settings.Language, "KeyFile");
    public string Password => Localizer.Text(_settings.Language, "Password");
    public string Show => Localizer.Text(_settings.Language, "Show");
    public string PasswordLayoutHelp => Localizer.Text(_settings.Language, "PasswordLayoutHelp");
    public bool AutoEnglishPasswordLayout
    {
        get => _settings.AutoEnglishPasswordLayout;
        set
        {
            if (_settings.AutoEnglishPasswordLayout == value) return;
            _settings.AutoEnglishPasswordLayout = value;
            Changed();
            PreferencesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public string Open => Localizer.Text(_settings.Language, "Open");
    public string Demo => Localizer.Text(_settings.Language, "Demo");
    public string DemoHelp => Localizer.Text(_settings.Language, "DemoHelp");
    public string DemoDatabasePlaceholder => Localizer.Text(_settings.Language, "DemoDatabasePlaceholder");
    public string DemoKeyFilePlaceholder => Localizer.Text(_settings.Language, "DemoKeyFilePlaceholder");
    public string Clear => Localizer.Text(_settings.Language, "Clear");
    public string RememberPathsCaption => Localizer.Text(_settings.Language, "RememberPaths");
    public string RememberSelectionCaption => Localizer.Text(_settings.Language, "RememberSelection");
    public string Forget => Localizer.Text(_settings.Language, "Forget");
    public string Records => Localizer.Text(_settings.Language, "Records");
    public string Filter => Localizer.Text(_settings.Language, "Filter");
    public string Group => Localizer.Text(_settings.Language, "Group");
    public string Title => Localizer.Text(_settings.Language, "Title");
    public string Print => Localizer.Text(_settings.Language, "Print");
    public string SelectAll => Localizer.Text(_settings.Language, "SelectAll");
    public string SelectNone => Localizer.Text(_settings.Language, "SelectNone");
    public string SelectToPrint => Localizer.Text(_settings.Language, "SelectToPrint");
    public string SelectedZero => Localizer.Text(_settings.Language, "SelectedZero");
    public string Icons => Localizer.Text(_settings.Language, "Icons");
    public string Apply => Localizer.Text(_settings.Language, "Apply");
    public string ReadOnlyNotice => Localizer.Text(_settings.Language, "ReadOnlyNotice");
    public string MappingTitle => Localizer.Text(_settings.Language, "MappingTitle");
    public string MappingHint => Localizer.Text(_settings.Language, "MappingHint");
    public string Scope => Localizer.Text(_settings.Language, "Scope");
    public string AllColumns => Localizer.Text(_settings.Language, "AllColumns");
    public string Position => Localizer.Text(_settings.Language, "Position");
    public string Field => Localizer.Text(_settings.Language, "Field");
    public string Section => Localizer.Text(_settings.Language, "Section");
    public string Priority => Localizer.Text(_settings.Language, "Priority");
    public string OutputGroup => Localizer.Text(_settings.Language, "OutputGroup");
    public string Format => Localizer.Text(_settings.Language, "Format");
    public string Visibility => Localizer.Text(_settings.Language, "Visibility");
    public string Label => Localizer.Text(_settings.Language, "Label");
    public string LongValue => Localizer.Text(_settings.Language, "LongValue");
    public string Grouping => Localizer.Text(_settings.Language, "Grouping");
    public string FieldOptions => Localizer.Text(_settings.Language, "FieldOptions");
    public string NotSelected => Localizer.Text(_settings.Language, "NotSelected");
    public string DesignTitle => Localizer.Text(_settings.Language, "DesignTitle");
    public string Templates => Localizer.Text(_settings.Language, "Templates");
    public string Layout => Localizer.Text(_settings.Language, "Layout");
    public string CardsPerRow => Localizer.Text(_settings.Language, "CardsPerRow");
    public string CardWidth => Localizer.Text(_settings.Language, "CardWidth");
    public string CardHeight => Localizer.Text(_settings.Language, "CardHeight");
    public string ValueFont => Localizer.Text(_settings.Language, "ValueFont");
    public string TitleFont => Localizer.Text(_settings.Language, "TitleFont");
    public string FontSize => Localizer.Text(_settings.Language, "FontSize");
    public string Colors => Localizer.Text(_settings.Language, "Colors");
    public string Background => Localizer.Text(_settings.Language, "Background");
    public string TextColor => Localizer.Text(_settings.Language, "TextColor");
    public string Rules => Localizer.Text(_settings.Language, "Rules");
    public string QR => Localizer.Text(_settings.Language, "QR");
    public string QRPrint => Localizer.Text(_settings.Language, "QRPrint");
    public string Notes => Localizer.Text(_settings.Language, "Notes");
    public string Cutting => Localizer.Text(_settings.Language, "Cutting");
    public string DuplexTitle => Localizer.Text(_settings.Language, "DuplexTitle");
    public string DuplexDescription => Localizer.Text(_settings.Language, "DuplexDescription");
    public string DuplexEnabled => Localizer.Text(_settings.Language, "DuplexEnabled");
    public string DuplexContinuation => Localizer.Text(_settings.Language, "DuplexContinuation");
    public string DuplexCombine => Localizer.Text(_settings.Language, "DuplexCombine");
    public string DuplexBack => Localizer.Text(_settings.Language, "DuplexBack");
    public string DuplexFlip => Localizer.Text(_settings.Language, "DuplexFlip");
    public string TestAlignment => Localizer.Text(_settings.Language, "TestAlignment");
    public string Calibration => Localizer.Text(_settings.Language, "Calibration");
    public string Preview => Localizer.Text(_settings.Language, "Preview");
    public string Mode => Localizer.Text(_settings.Language, "Mode");
    public string Page => Localizer.Text(_settings.Language, "Page");
    public string Side => Localizer.Text(_settings.Language, "Side");
    public string Front => Localizer.Text(_settings.Language, "Front");
    public string Back => Localizer.Text(_settings.Language, "Back");
    public string Mono => Localizer.Text(_settings.Language, "Mono");
    public string Zoom => Localizer.Text(_settings.Language, "Zoom");
    public string PreviewPlaceholder => Localizer.Text(_settings.Language, "PreviewPlaceholder");
    public string PreviewWarning => Localizer.Text(_settings.Language, "PreviewWarning");
    public string Printer => Localizer.Text(_settings.Language, "Printer");
    public string PrintFront => Localizer.Text(_settings.Language, "PrintFront");
    public string PrintBack => Localizer.Text(_settings.Language, "PrintBack");
    public string Paper => Localizer.Text(_settings.Language, "Paper");
    public string YubiEnabledCaption => Localizer.Text(_settings.Language, "YubiEnabledCaption");
    public string YubiSlotCaption => Localizer.Text(_settings.Language, "YubiSlotCaption");
    public string YubiShortCaption => Localizer.Text(_settings.Language, "YubiShortCaption");
    public string YubiKeyHelp => Localizer.Text(_settings.Language, "YubiKeyHelp");
    public string YubiDeviceHelp => Localizer.Text(_settings.Language, "YubiDeviceHelp");
    public string YubiRefreshHelp => Localizer.Text(_settings.Language, "YubiRefreshHelp");
    public string YubiShortHelp => Localizer.Text(_settings.Language, "YubiShortHelp");
    public string Status => BuildOperationalStatus();
}


