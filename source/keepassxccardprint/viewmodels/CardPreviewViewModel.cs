using KeePassXCCardPrint.Platforms;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using KeePassXCCardPrint.Models;
using KeePassXCCardPrint.Rendering;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.ViewModels;

public sealed partial class ShellViewModel
{
    private CardAppearance _appearance = CardAppearanceStore.Load();
    private readonly DispatcherTimer _previewTimer = new() { Interval=TimeSpan.FromMilliseconds(70) };
    // A live numeric hold must produce intermediate preview frames, not starve
    // every build by cancelling it before completion.
    private bool _appearanceBuildRunning;
    private bool _appearanceBuildQueued;

    // Bounded concurrency: layout already distributes independent cards across cores.
    // Navigation may replace an obsolete render without waiting for its completion.
    private readonly SemaphoreSlim _previewRenderGate = new(Math.Clamp(Environment.ProcessorCount-1,1,4));
    private readonly DispatcherTimer _previewStaleTimer = new() { Interval=TimeSpan.FromMilliseconds(150) };
    // One UI-thread clock for lightweight opacity animation. The Skia print
    // pictures are never re-recorded, re-encoded or decoded by the fade.
    private readonly DispatcherTimer _previewFadeTimer = new() { Interval=TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _previewFadeClock = new();
    private int _activeFadeDurationMs;
    private long _previewCommittedRevision=-1;
    private bool _hasPrintSelection;
    private long _previewCacheEpoch;
    private readonly SecurePreviewSceneCache _previewCache=new();
    private readonly SemaphoreSlim _prefetchGate=new(1,1);
    private CancellationTokenSource? _prefetchCancellation;
    private bool _previewUpdating;
    private bool _previewShowSheet;
    private IReadOnlyList<CardPrintSheet> _previewSheets=[];
    private int _previewSheetIndex;
    private double _previewZoomPercent=100;
    private double _previewDisplayWidth=410;
    private double _previewDisplayHeight=600;
    private SkiaPreviewScene? _previewPreviousScene;
    private double _previewOldOpacity=1;
    private bool _previewExporting;
    public ObservableCollection<string> PaperFormats { get; } =
        new(new[] { "A3", "A4", "A5", "A6", "Letter", "Legal" });
    private CancellationTokenSource? _previewCancellation;
    private long _previewRevision;
    private sealed record PreviewBuildResult(IReadOnlyList<PhysicalCard> Cards,
        IReadOnlyList<CardPrintSheet> Sheets,int CardIndex,int SheetIndex,bool BackSide,
        bool HasPrintSelection,SkiaPreviewScene? Scene);

    private readonly List<PhysicalCard> _cardParts = [];
    private SkiaPreviewScene? _previewScene;
    private int _cardIndex;
    private bool _backSide;
    private string _previewError="";
    private string _designMessage="";
    private string _selectedDesignCategory="Геометрия";
    private string _templateName="";
    private string? _selectedTemplate;
    private readonly Dictionary<string, string> _personalTemplatePaths = new(StringComparer.Ordinal);
    private enum TemplateOrigin { Unlinked, BuiltIn, Personal }
    private TemplateOrigin _activeTemplateOrigin = TemplateOrigin.Unlinked;
    private string? _activePersonalTemplatePath;
    private string _activeTemplateName = "";
    private bool _templateDirty;
    public bool HasUnsavedTemplateChanges => _templateDirty || MappingHasPendingChanges;
    public string? ActiveTemplateSelection => _activeTemplateOrigin switch
    {
        TemplateOrigin.BuiltIn => BuiltinLabel(_appearance.Style),
        TemplateOrigin.Personal when _activePersonalTemplatePath is not null =>
            _personalTemplatePaths.FirstOrDefault(item => string.Equals(item.Value,
                _activePersonalTemplatePath, PlatformServices.Current.FileNameComparison)).Key,
        _ => null
    };
    public bool CanEditAppearance => _activeTemplateOrigin != TemplateOrigin.BuiltIn;
    public bool CanSaveTemplateChanges => _activeTemplateOrigin == TemplateOrigin.Personal &&
        !MappingHasInvalidDrafts && HasUnsavedTemplateChanges;
    public string ActiveTemplateNotice => Localizer.ResolveLanguage(_settings.Language) switch
    {
        "en" => _activeTemplateOrigin switch
        {
            TemplateOrigin.BuiltIn => $"Built-in · {_activeTemplateName} — read-only. Create your own template to edit either side.",
            TemplateOrigin.Personal => $"My · {_activeTemplateName} — both sides are editable." +
                (HasUnsavedTemplateChanges ? " Unsaved changes." : " All changes saved."),
            _ => "Current design without a template. Create a personal template to save edits to both sides."
        },
        "uk" => _activeTemplateOrigin switch
        {
            TemplateOrigin.BuiltIn => $"Вбудований · {_activeTemplateName} — лише перегляд. Створіть власний шаблон для редагування обох боків.",
            TemplateOrigin.Personal => $"Мій · {_activeTemplateName} — обидва боки доступні для редагування." +
                (HasUnsavedTemplateChanges ? " Є незбережені зміни." : " Усі зміни збережені."),
            _ => "Поточне оформлення без шаблона. Створіть власний шаблон для збереження змін обох боків."
        },
        _ => _activeTemplateOrigin switch
        {
            TemplateOrigin.BuiltIn => $"Встроенный · {_activeTemplateName} — только просмотр. Создайте свой шаблон, чтобы изменять лицо и оборот.",
            TemplateOrigin.Personal => $"Мой · {_activeTemplateName} — лицо и оборот доступны для правки." +
                (HasUnsavedTemplateChanges ? " Есть несохранённые изменения." : " Все изменения сохранены."),
            _ => "Текущее оформление без шаблона. Создайте свой шаблон для сохранения и редактирования обеих сторон."
        }
    };
    private void NotifyTemplateState()
    {
        foreach (var field in AppearanceFields) field.IsReadOnly = !CanEditAppearance;
        MappingEditor?.SetEditable(CanEditAppearance);
        Changed(nameof(CanEditAppearance));
        Changed(nameof(MappingCanEdit));
        Changed(nameof(CanSaveTemplateChanges));
        Changed(nameof(ActiveTemplateNotice));
    }
    private void AppearanceEdited()
    {
        if (!CanEditAppearance) return;
        _templateDirty = true;
        DesignMessage = "";
        NotifyTemplateState();
        ScheduleCardPreview();
    }
    private static readonly (string Code, string Label)[] BuiltInTemplates =
    [
        ("archive", "Встроенный · Элегантный архив"),
        ("technical", "Встроенный · Инженерная карта"),
        ("table", "Встроенный · QR и восстановление")
    ];
    private string BuiltinName(string code) => Localizer.ResolveLanguage(_settings.Language) switch
    {
        "uk" => code switch {"archive"=>"Елегантний архів","technical"=>"Інженерна картка","table"=>"QR і відновлення",_=>code},
        "en" => code switch {"archive"=>"Elegant archive","technical"=>"Engineering card","table"=>"QR and recovery",_=>code},
        _ => BuiltInTemplates.First(x=>x.Code==code).Label.Split('·').Last().Trim()
    };
    private string BuiltinLabel(string code) =>
        (Localizer.ResolveLanguage(_settings.Language) switch {"uk"=>"Вбудований","en"=>"Built-in",_=>"Встроенный"})
        + " · " + BuiltinName(code);
    private string PersonalPrefix => Localizer.ResolveLanguage(_settings.Language) switch {"uk"=>"Мій · ","en"=>"My · ",_=>"Мой · "};
    private string CopySuffix => Localizer.ResolveLanguage(_settings.Language) switch {"uk"=>" - копія","en"=>" - copy",_=>" - копия"};

    public ObservableCollection<AppearanceField> AppearanceFields { get; } = new();
    public ObservableCollection<AppearanceField> VisibleAppearanceFields { get; } = new();
    public ObservableCollection<AppearanceField> DuplexAppearanceFields { get; } = new();
    public ObservableCollection<string> DesignCategories { get; } = new()
    { "Геометрия", "Шапка и подвал", "Шрифты", "Поля и содержимое", "Иконки и цвета", "Фон и декор", "Коды восстановления", "QR-коды TOTP", "Две стороны" };
    public ObservableCollection<string> DuplexDesignCategories { get; } = new()
    { "Основное", "Пустой оборот", "Фоновая иконка", "Линии оборота" };
    // Start at the controls that affect the blank reverse design.
    private string _selectedDuplexDesignCategory="Пустой оборот";
    public AppearanceField EmptyBackHeaderField {get;private set;} = null!;
    public AppearanceField EmptyBackFooterField {get;private set;} = null!;
    public AppearanceField EmptyBackFillField {get;private set;} = null!;
    public string SelectedDuplexDesignCategory
    {
        get => _selectedDuplexDesignCategory;
        set
        {
            if(value is null || _selectedDuplexDesignCategory==value)return;
            _selectedDuplexDesignCategory=value;
            Changed();
            RefreshDuplexAppearanceFields();
        }
    }
    private void RefreshDuplexAppearanceFields()
    {
        DuplexAppearanceFields.Clear();
        var group=AppearanceTranslations.CanonicalGroup(_selectedDuplexDesignCategory);
        if (group=="Основное") group="Две стороны";
        foreach(var item in AppearanceFields.Where(x=>x.Group==group &&
            x.Key is not ("duplex_back_fill" or "empty_back_show_header" or "empty_back_show_footer")))
            DuplexAppearanceFields.Add(item);
    }
    public ObservableCollection<string> TemplateNames { get; } = new();
    public string SelectedDesignCategory
    {
        get => _selectedDesignCategory;
        set
        {
            if(value is null || _selectedDesignCategory==value)return;
            _selectedDesignCategory=value;Changed();
            VisibleAppearanceFields.Clear();
            foreach (var appearanceItem in AppearanceFields.Where(item =>
                item.Group == AppearanceTranslations.CanonicalGroup(value)))
                VisibleAppearanceFields.Add(appearanceItem);
        }
    }
    public string TemplateName {get=>_templateName;set{_templateName=value??"";Changed();}}
    public string? SelectedTemplate
    {
        get => _selectedTemplate;
        set { if (_selectedTemplate == value) return; _selectedTemplate = value; Changed(); }
    }
    public string DesignMessage
    {
        get => UiText.Tr(_designMessage,_settings.Language);
        private set
        {
            if (_designMessage == value) return;
            _designMessage = value;
            Changed();
            Changed(nameof(HasDesignMessage));
        }
    }
    public bool HasDesignMessage => !string.IsNullOrWhiteSpace(_designMessage);
    public SkiaPreviewScene? PreviewScene => _previewScene;
    public SkiaPreviewScene? PreviewPreviousScene => _previewPreviousScene;
    public double PreviewOldOpacity { get=>_previewOldOpacity;private set { _previewOldOpacity=value;Changed(); } }
    public bool PreviewUpdating { get=>_previewUpdating;private set { if(_previewUpdating==value)return;_previewUpdating=value;Changed();Changed(nameof(PreviewEmpty)); } }
    public string PreviewUpdateLabel => Localizer.Text(_settings.Language,"PreviewRebuilding");
    public string PreviewNoCardsLabel => Localizer.Text(_settings.Language,"PreviewNoCards");
    public double PreviewDisplayWidth { get=>_previewDisplayWidth;private set { _previewDisplayWidth=value;Changed(); } }
    public double PreviewDisplayHeight { get=>_previewDisplayHeight;private set { _previewDisplayHeight=value;Changed(); } }
    public string PreviewZoomCaption => $"{_previewZoomPercent:0.#}%";
    // The preference is saved in ui-shell.json, never alongside KDBX secrets.
    public ObservableCollection<string> PreviewWheelStepOptions { get; } =
        new(new[] { "2%", "4%", "6%", "8%", "12%", "18%", "25%", "30%" });
    public string SelectedPreviewWheelStep
    {
        get=>$"{_settings.PreviewWheelStepPercent}%";
        set
        {
            if(string.IsNullOrEmpty(value) ||
               !int.TryParse(value.TrimEnd('%'),out int step) ||
               !PreviewWheelStepOptions.Contains(value) ||
               _settings.PreviewWheelStepPercent==step)return;
            _settings.PreviewWheelStepPercent=step;
            Changed();
        }
    }
    public double PreviewZoomPercent
    {
        get=>_previewZoomPercent;
        set
        {
            var zoom=Math.Clamp(value,5,600);
            if(_previewZoomPercent==zoom)return;
            _previewZoomPercent=zoom;
            _settings.PreviewZoomPercent=(int)Math.Round(zoom);
            Changed();
            Changed(nameof(PreviewZoomCaption));
            // Zoom is a render-only transform owned by the preview viewport.
            // Do not resize the document or ask ScrollViewer to re-layout it.
        }
    }
    public bool PreviewShowSheet
    {
        get=>_previewShowSheet;
        set
        {
            if(_previewShowSheet==value)return;
            _previewShowSheet=value;
            _settings.PreviewShowSheets=value;
            Changed();
            UpdatePreviewDisplayWidth();
            ScheduleCardPreview();
        }
    }
    public bool PreviewAnimationEnabled
    {
        get=>_settings.PreviewAnimation;
        set
        {
            if(_settings.PreviewAnimation==value)return;
            _settings.PreviewAnimation=value;
            if(!value)ResetTransition();
            Changed();
            PreferencesChanged?.Invoke(this,EventArgs.Empty);
        }
    }
    public bool PreviewAnimateAfterDelay
    {
        get=>_settings.PreviewAnimateAfterDelay;
        set
        {
            if(_settings.PreviewAnimateAfterDelay==value)return;
            _settings.PreviewAnimateAfterDelay=value;
            Changed();PreferencesChanged?.Invoke(this,EventArgs.Empty);
        }
    }
    public bool PreviewAnimateNavigation
    {
        get=>_settings.PreviewAnimateNavigation;
        set
        {
            if(_settings.PreviewAnimateNavigation==value)return;
            _settings.PreviewAnimateNavigation=value;
            Changed();PreferencesChanged?.Invoke(this,EventArgs.Empty);
        }
    }
    public bool PreviewAnimateSideChange
    {
        get=>_settings.PreviewAnimateSideChange;
        set
        {
            if(_settings.PreviewAnimateSideChange==value)return;
            _settings.PreviewAnimateSideChange=value;
            Changed();PreferencesChanged?.Invoke(this,EventArgs.Empty);
        }
    }
    public double PreviewAnimationDurationMs
    {
        get=>_settings.PreviewAnimationDurationMs;
        set
        {
            int milliseconds=Math.Clamp((int)Math.Round(value/10d)*10,50,500);
            if(_settings.PreviewAnimationDurationMs==milliseconds)return;
            _settings.PreviewAnimationDurationMs=milliseconds;
            Changed();Changed(nameof(PreviewAnimationDurationCaption));
            PreferencesChanged?.Invoke(this,EventArgs.Empty);
        }
    }
    public string PreviewAnimationDurationCaption=>UiText.Tr($"{_settings.PreviewAnimationDurationMs} мс",_settings.Language);
    public bool PreviewMonochrome
    {
        get=>_settings.PreviewMonochrome;
        set
        {
            if(_settings.PreviewMonochrome==value)return;
            _settings.PreviewMonochrome=value;
            Changed(); // Skia color matrix is applied at drawing time, no card rerender.
            PreferencesChanged?.Invoke(this,EventArgs.Empty);
        }
    }
    public string SelectedPaperFormat
    {
        get=>_settings.PaperFormat;
        set
        {
            if(_settings.PaperFormat==value || !PaperFormats.Contains(value))return;
            _settings.PaperFormat=value;
            _settings.PrintStartSlot=1;
            Changed();
            Changed(nameof(PrintStartSlotText));
            ScheduleCardPreview();
        }
    }
    public string PrintStartSlotText
    {
        get=>_settings.PrintStartSlot.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if(!int.TryParse(value,out int number) || number<1 || number>36 ||
                _settings.PrintStartSlot==number)return;
            _settings.PrintStartSlot=number;
            Changed();
            ScheduleCardPreview();
        }
    }
    public bool PrintLandscape
    {
        get=>_settings.PrintLandscape;
        set {if(_settings.PrintLandscape==value)return;_settings.PrintLandscape=value;Changed();ScheduleCardPreview();}
    }
    public bool PrintShortEdgeFlip
    {
        get=>_settings.PrintShortEdgeFlip;
        set {if(_settings.PrintShortEdgeFlip==value)return;_settings.PrintShortEdgeFlip=value;Changed();ScheduleCardPreview();}
    }
    // Export and printing are the same sensitive operation: neither may start
    // without an open KDBX, at least one selected record, and a current layout.
    // Do not rely solely on a stale preview scene when a vault is closing/loading.
    public bool PrintReady => _snapshot is not null && !Busy && _selectedCount>0 &&
        _hasPrintSelection && _previewSheets.Count>0 && !_previewExporting &&
        _previewCommittedRevision==_previewRevision && !_appearanceBuildQueued && !_previewTimer.IsEnabled;
    public bool PreviewEmpty => _previewScene is null && !_previewUpdating &&
        string.IsNullOrWhiteSpace(_previewError);
    public string PreviewCardCount => _previewShowSheet
        ? (_previewSheets.Count==0?"— / —":$"{_previewSheetIndex+1} / {_previewSheets.Count}")
        : (_cardParts.Count==0?"— / —":$"{_cardIndex+1} / {_cardParts.Count}");
    public string PreviewFaceLabel => UiText.Tr(_previewShowSheet
        ? (_previewSheets.Count>0 && _previewSheets[_previewSheetIndex].Reverse?"Лист · оборот":"Лист · лицо")
        : (_backSide?"Оборотная сторона":"Лицевая сторона"),_settings.Language);
    public bool PreviewHasCard => _cardParts.Count>0;
    public bool PreviewDuplexAvailable => _appearance.DuplexEnabled && _cardParts.Count>0;
    public bool PreviewIsFront => !_backSide;
    public bool PreviewIsBack => _backSide && _appearance.DuplexEnabled;
    public bool PreviewCanGoBack => _previewShowSheet ? _previewSheetIndex>0 :
        CardPreviewSequence.CanStep(_cardParts.Count,_cardIndex,_backSide,_appearance.DuplexEnabled,-1);
    public bool PreviewCanGoNext => _previewShowSheet ? _previewSheetIndex+1<_previewSheets.Count :
        CardPreviewSequence.CanStep(_cardParts.Count,_cardIndex,_backSide,_appearance.DuplexEnabled,+1);
    public string PreviewError => UiText.Tr(_previewError,_settings.Language);

    public void SetPreviewNotice(string message) { _previewError=message;Changed(nameof(PreviewError)); }
    private void UpdatePreviewDisplayWidth()
    {
        var paper=PaperGeometry.Resolve(_settings.PaperFormat,_settings.PrintLandscape);
        double width=_previewShowSheet?paper.WidthMm:_appearance.WidthMm;
        double height=_previewShowSheet?paper.HeightMm:_appearance.HeightMm;
        double factor=_previewShowSheet?1.9:5.8;
        // Unzoomed document dimensions are stable during wheel interaction.
        // Avalonia's ScaleTransform changes the visual scale independently of layout.
        var displayWidth=width*factor;
        PreviewDisplayWidth=displayWidth;
        PreviewDisplayHeight=displayWidth*height/width;
    }
    public void ZoomPreview(double wheelDelta) =>
        PreviewZoomPercent=100*PreviewViewportGeometry.WheelZoom(
            PreviewZoomPercent/100d,wheelDelta,_settings.PreviewWheelStepPercent);

    private void InitializeCardPreview()
    {
        _previewTimer.Tick+=(_,_)=>{_previewTimer.Stop(); _ = RebuildCardPreviewAsync();};
        _previewFadeTimer.Tick+=(_,_)=>OnPreviewFadeFrame();
        _previewStaleTimer.Tick+=(_,_)=>
        {
            _previewStaleTimer.Stop();
            if(_previewCommittedRevision!=_previewRevision)
                PreviewUpdating=true;
        };
        _previewZoomPercent=Math.Clamp(_settings.PreviewZoomPercent,5,600);
        _previewShowSheet=_settings.PreviewShowSheets;
        RestorePersonalTemplateSelection();
        RebuildAppearanceFields();
        ReloadTemplates();
        RelocalizeAppearance();
        UpdatePreviewDisplayWidth();
        ScheduleCardPreview();
    }
    private void RelocalizeAppearance()
    {
        var group = AppearanceTranslations.CanonicalGroup(_selectedDesignCategory);
        var duplexGroup = AppearanceTranslations.CanonicalGroup(_selectedDuplexDesignCategory);
        if (_activeTemplateOrigin == TemplateOrigin.BuiltIn)
            _activeTemplateName = BuiltinName(_appearance.Style);
        ReloadTemplates();
        foreach (var field in AppearanceFields) field.Relocalize(_settings.Language);
        var categories = DesignCategories.Select(AppearanceTranslations.CanonicalGroup).ToArray();
        var duplexCategories = DuplexDesignCategories.Select(AppearanceTranslations.CanonicalGroup).ToArray();
        DesignCategories.Clear();
        foreach (var category in categories)
            DesignCategories.Add(AppearanceTranslations.Group(category, _settings.Language));
        DuplexDesignCategories.Clear();
        foreach (var category in duplexCategories)
            DuplexDesignCategories.Add(AppearanceTranslations.Group(category, _settings.Language));
        _selectedDesignCategory = AppearanceTranslations.Group(group, _settings.Language);
        _selectedDuplexDesignCategory = AppearanceTranslations.Group(duplexGroup, _settings.Language);
        Changed(nameof(SelectedDesignCategory));
        Changed(nameof(SelectedDuplexDesignCategory));
        VisibleAppearanceFields.Clear();
        foreach (var field in AppearanceFields.Where(x => x.Group == group))
            VisibleAppearanceFields.Add(field);
        RefreshDuplexAppearanceFields();
        NotifyTemplateState();
    }

    private void RebuildAppearanceFields()
    {
        AppearanceFields.Clear();
        DuplexAppearanceFields.Clear();
        foreach(var f in AppearanceDescriptor.Create(_appearance,_settings.Language))
        {
            f.IsReadOnly = !CanEditAppearance;
            f.ValueChanged+=(_,_)=>AppearanceEdited();
            AppearanceFields.Add(f);
        }
        EmptyBackHeaderField=AppearanceFields.First(x=>x.Key=="empty_back_show_header");
        EmptyBackFooterField=AppearanceFields.First(x=>x.Key=="empty_back_show_footer");
        EmptyBackFillField=AppearanceFields.First(x=>x.Key=="duplex_back_fill");
        Changed(nameof(EmptyBackHeaderField));
        Changed(nameof(EmptyBackFooterField));
        Changed(nameof(EmptyBackFillField));
        RefreshDuplexAppearanceFields();
        var selected=_selectedDesignCategory;
        _selectedDesignCategory="";
        SelectedDesignCategory=selected;
    }
    private void RestorePersonalTemplateSelection()
    {
        var filename = CardAppearanceStore.ReadActivePersonalTemplate();
        if (filename is null)
        {
            // With only three built-ins, an obsolete former preset falls back
            // to archive. Personal files with legacy styles remain untouched.
            var builtin = BuiltInTemplates.FirstOrDefault(t => t.Code == _appearance.Style);
            if (builtin.Code is null) builtin = BuiltInTemplates[0];
            if (builtin.Code is not null)
            {
                _appearance = CardAppearancePresets.Create(builtin.Code);
                _activeTemplateOrigin = TemplateOrigin.BuiltIn;
                _activeTemplateName = BuiltinName(builtin.Code);
                TemplateName = _activeTemplateName + CopySuffix;
                NotifyTemplateState();
            }
            return;
        }
        try
        {
            var path = CardAppearanceStore.Templates().FirstOrDefault(p =>
                string.Equals(Path.GetFileName(p), filename, PlatformServices.Current.FileNameComparison));
            if (path is null) return;
            var name = CardAppearanceStore.ReadTemplateName(path);
            _activeTemplateOrigin = TemplateOrigin.Personal;
            _activePersonalTemplatePath = path;
            _activeTemplateName = name;
            // Load the saved complete template, never an abandoned draft cache.
            _appearance = CardAppearanceStore.ReadTemplate(path);
            _templateDirty = false;
            NotifyTemplateState();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or InvalidDataException) { }
    }
    private void ReloadTemplates()
    {
        var priorSelection = _selectedTemplate;
        _personalTemplatePaths.Clear();
        TemplateNames.Clear();
        foreach (var (code, _) in BuiltInTemplates) TemplateNames.Add(BuiltinLabel(code));

        foreach (var path in CardAppearanceStore.Templates())
        {
            string name;
            try { name = CardAppearanceStore.ReadTemplateName(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            { continue; }
            var label = PersonalPrefix + name;
            if (_personalTemplatePaths.ContainsKey(label))
                label += " (" + Path.GetFileNameWithoutExtension(path) + ")";
            _personalTemplatePaths[label] = path;
            TemplateNames.Add(label);
        }
        var activePersonal = _activePersonalTemplatePath is null ? null :
            _personalTemplatePaths.FirstOrDefault(kv =>
                string.Equals(kv.Value, _activePersonalTemplatePath, PlatformServices.Current.FileNameComparison)).Key;
        if (priorSelection is not null && TemplateNames.Contains(priorSelection))
            SelectedTemplate = priorSelection;
        else if (activePersonal is not null)
            SelectedTemplate = activePersonal;
        else if (_activeTemplateOrigin == TemplateOrigin.BuiltIn)
            SelectedTemplate = BuiltinLabel(_appearance.Style);
        else
            SelectedTemplate = null; // Built-in presets are not selected while editing a custom working copy.
    }

    // Persist only the selected, fully saved configuration for reopening.
    // Draft changes never persist separately or overwrite the full template.
    public void SaveCardAppearance()
    {
        if (HasUnsavedTemplateChanges) return;
        try
        {
            CardAppearanceStore.Save(_appearance);
            CardAppearanceStore.SaveActivePersonalTemplate(_activeTemplateOrigin == TemplateOrigin.Personal
                ? Path.GetFileName(_activePersonalTemplatePath!) : null);
        }
        catch(Exception e) when (e is IOException or UnauthorizedAccessException)
        { DesignMessage="Не удалось сохранить текущие настройки оформления."; }
    }

    public bool LoadCardTemplate(bool discardUnsavedChanges = false)
    {
        if (string.IsNullOrEmpty(_selectedTemplate)) return false;
        if (HasUnsavedTemplateChanges && !discardUnsavedChanges)
        {
            DesignMessage = "Есть несохранённые изменения. Подтвердите переключение шаблона.";
            return false;
        }
        try
        {
            var builtin = BuiltInTemplates.FirstOrDefault(t => BuiltinLabel(t.Code) == _selectedTemplate);
            if (builtin.Code is not null)
            {
                _appearance = CardAppearancePresets.Create(builtin.Code);
                _activeTemplateOrigin = TemplateOrigin.BuiltIn;
                _activePersonalTemplatePath = null;
                _activeTemplateName = BuiltinName(builtin.Code);
                TemplateName = _activeTemplateName + CopySuffix;
                LoadTemplateMapping(CardAppearancePresets.CreateMapping(builtin.Code));
            }
            else if (_personalTemplatePaths.TryGetValue(_selectedTemplate, out var path))
            {
                // Read and validate BOTH components before touching active state.
                var appearance = CardAppearanceStore.ReadTemplate(path);
                var mapping = CardAppearanceStore.ReadTemplateMapping(path);
                var name = CardAppearanceStore.ReadTemplateName(path);
                _appearance = appearance;
                _activeTemplateOrigin = TemplateOrigin.Personal;
                _activePersonalTemplatePath = path;
                _activeTemplateName = name;
                TemplateName = name + CopySuffix;
                LoadTemplateMapping(mapping);
            }
            else return false;
            _templateDirty = false;
            RebuildAppearanceFields();
            SaveCardAppearance(); // Cache only the selected, complete template.
            NotifyTemplateState();
            ScheduleCardPreview();
            // ActiveTemplateNotice already communicates origin and editability.
            DesignMessage = "";
            return true;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or InvalidDataException)
        {
            DesignMessage = "Не удалось открыть шаблон.";
            return false;
        }
    }

    public void SaveCardTemplate()
    {
        if (MappingHasInvalidDrafts)
        {
            DesignMessage = "Исправьте ошибки маппинга перед созданием шаблона.";
            return;
        }
        try
        {
            var name = _templateName.Trim();
            var path = CardAppearanceStore.CreatePersonalTemplate(name, _appearance, _map);
            _activeTemplateOrigin = TemplateOrigin.Personal;
            _activePersonalTemplatePath = path;
            _activeTemplateName = name;
            _templateDirty = false;
            MarkTemplateMappingSaved();
            SaveCardAppearance();
            ReloadTemplates();
            SelectedTemplate = _personalTemplatePaths.First(kv =>
                string.Equals(kv.Value, path, PlatformServices.Current.FileNameComparison)).Key;
            NotifyTemplateState();
            DesignMessage = "";
        }
        catch(ArgumentException e) { DesignMessage=e.Message; }
        catch(IOException e) { DesignMessage=e.Message; }
        catch(UnauthorizedAccessException) { DesignMessage="Нет прав для сохранения шаблона."; }
    }

    public void SaveChangesToTemplate()
    {
        if (_activeTemplateOrigin != TemplateOrigin.Personal || _activePersonalTemplatePath is null)
        {
            DesignMessage="Встроенные шаблоны не изменяются. Сначала создайте свой шаблон.";
            return;
        }
        if (MappingHasInvalidDrafts)
        {
            DesignMessage = "Исправьте ошибки маппинга перед сохранением шаблона.";
            return;
        }
        try
        {
            // One atomic write: both sides of the card and ALL mapping rules.
            CardAppearanceStore.UpdatePersonalTemplate(_activePersonalTemplatePath, _appearance, _map);
            _templateDirty = false;
            MarkTemplateMappingSaved();
            SaveCardAppearance();
            NotifyTemplateState();
            DesignMessage = "";
        }
        catch (Exception e) when(e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or System.Text.Json.JsonException)
        { DesignMessage="Не удалось сохранить изменения в свой шаблон."; }
    }

    private void PreviewStep(int direction)
    {
        if(_previewShowSheet)
        {
            int next=_previewSheetIndex+direction;
            if(next<0 || next>=_previewSheets.Count)return;
            _previewSheetIndex=next;
            if(_previewSheets[next].Slots.Count>0)
                _cardIndex=_previewSheets[next].Slots[0].CardIndex;
            _backSide=_previewSheets[next].Reverse;
        }
        else
        {
            bool duplex=_appearance.DuplexEnabled;
            if(!CardPreviewSequence.CanStep(_cardParts.Count,_cardIndex,_backSide,duplex,direction))return;
            int position=CardPreviewSequence.Position(_cardIndex,_backSide,duplex)+direction;
            (_cardIndex,_backSide)=CardPreviewSequence.At(position,duplex);
        }
        _ = RenderExistingCardAsync(PreviewTransitionReason.Navigation);
        NotifyCardPreview();
    }
    public void PreviewPrevious()=>PreviewStep(-1);
    public void PreviewNext()=>PreviewStep(+1);
    public void PreviewFront()
    {
        if(_previewShowSheet)
        {
            if(_previewSheets.Count==0 || !_previewSheets[_previewSheetIndex].Reverse)
            { Changed(nameof(PreviewIsFront)); Changed(nameof(PreviewIsBack)); return; }
            _previewSheetIndex--;
            _backSide=false;
        }
        else {if(!_backSide){ Changed(nameof(PreviewIsFront)); Changed(nameof(PreviewIsBack));return; }_backSide=false;}
        _ = RenderExistingCardAsync(PreviewTransitionReason.SideChange);NotifyCardPreview();
    }
    public void PreviewBack()
    {
        if(_previewShowSheet)
        {
            if(_previewSheetIndex+1>=_previewSheets.Count || _previewSheets[_previewSheetIndex].Reverse ||
                !_previewSheets[_previewSheetIndex+1].Reverse)
            { Changed(nameof(PreviewIsFront)); Changed(nameof(PreviewIsBack)); return; }
            _previewSheetIndex++;
            _backSide=true;
        }
        else {if(_backSide){ Changed(nameof(PreviewIsFront)); Changed(nameof(PreviewIsBack));return; }_backSide=true;}
        _ = RenderExistingCardAsync(PreviewTransitionReason.SideChange);NotifyCardPreview();
    }

    private CancellationTokenSource StartPreviewTask(out long revision,bool announceStale=true)
    {
        _previewCancellation?.Cancel();
        _prefetchCancellation?.Cancel();
        var current=new CancellationTokenSource();
        _previewCancellation=current;
        revision=++_previewRevision;
        Changed(nameof(PrintReady));
        _previewStaleTimer.Stop();
        if(announceStale)_previewStaleTimer.Start();
        return current;
    }

    public void ScheduleCardPreview()
    {
        UpdateUrlPreviewStatus();
        // A filter changes visibility, NOT print selection. Never preview an
        // arbitrary visible vault entry when no checkbox is selected.
        // Clear synchronously to avoid briefly exposing the previous card while
        // a debounce timer or background layout build is pending.
        if (!VaultRecords.Any(r => r.Selected))
        {
            CloseCardPreview();
            return;
        }
        _prefetchCancellation?.Cancel();
        _previewCacheEpoch++;
        _previewCache.Clear();
        if (_appearanceBuildRunning)
        {
            // Don't cancel every frame on held steppers. Let the current
            // snapshot appear and immediately queue a render of the latest state.
            _appearanceBuildQueued=true;
        }
        else
        {
            _previewCancellation?.Cancel();
            _previewRevision++;
            _previewTimer.Stop();
            _previewTimer.Start();
        }
        Changed(nameof(PrintReady));
        _previewStaleTimer.Stop();
        if (_previewScene is null) PreviewUpdating=true;
        else _previewStaleTimer.Start();
    }
    private void OnPreviewFadeFrame()
    {
        if(_previewPreviousScene is null)
        {
            CompletePreviewFade();
            return;
        }
        double t=Math.Clamp(_previewFadeClock.Elapsed.TotalMilliseconds/
            Math.Max(1,_activeFadeDurationMs),0,1);
        PreviewOldOpacity=PreviewTransitionPolicy.OldOpacity(t);
        if(t>=1)CompletePreviewFade();
    }

    private void CompletePreviewFade()
    {
        _previewFadeTimer.Stop();
        _previewFadeClock.Stop();
        PreviewOldOpacity=0;
        var old=_previewPreviousScene;
        _previewPreviousScene=null;
        Changed(nameof(PreviewPreviousScene));
        old?.Dispose();
    }

    private void ResetTransition(bool keepDominant=false)
    {
        _previewFadeTimer.Stop();
        _previewFadeClock.Stop();
        // If the user navigates again during a fade, start from the picture that
        // was visually dominant instead of flashing to an invisible intermediate.
        if(keepDominant && _previewPreviousScene is not null && _previewOldOpacity>.5)
        {
            var discarded=_previewScene;
            _previewScene=_previewPreviousScene;
            _previewPreviousScene=null;
            Changed(nameof(PreviewScene));
            Changed(nameof(PreviewPreviousScene));
            discarded?.Dispose();
        }
        else
        {
            var old=_previewPreviousScene;
            _previewPreviousScene=null;
            Changed(nameof(PreviewPreviousScene));
            old?.Dispose();
        }
        PreviewOldOpacity=0;
    }
    public void CloseCardPreview()
    {
        _previewTimer.Stop();
        _previewStaleTimer.Stop();
        _previewCancellation?.Cancel();
        _prefetchCancellation?.Cancel();
        _previewRevision++;
        _previewCommittedRevision=-1;
        _appearanceBuildQueued=false;
        _hasPrintSelection=false;
        _previewCacheEpoch++;
        _previewCache.Clear();
        _cardParts.Clear();
        _previewSheets=[];
        _cardIndex=0;
        _previewSheetIndex=0;
        _backSide=false;
        _previewError="";
        ResetTransition();
        _previewScene?.Dispose();
        _previewScene=null;
        Changed(nameof(PreviewScene));
        PreviewUpdating=false;
        NotifyCardPreview();
    }
    private void NotifyCardPreview()
    {
        foreach(var name in new[]{nameof(PreviewCardCount),nameof(PreviewFaceLabel),nameof(PreviewEmpty),
                          nameof(PreviewHasCard),nameof(PreviewDuplexAvailable),nameof(PreviewIsFront),nameof(PreviewIsBack),
                          nameof(PreviewCanGoBack),nameof(PreviewCanGoNext),
                          nameof(PreviewError),nameof(PrintReady)}) Changed(name);
    }

    private static CardAppearance SnapshotAppearance(CardAppearance appearance)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(appearance,CardAppearanceJsonContext.Default.CardAppearance);
        return JsonSerializer.Deserialize(bytes,CardAppearanceJsonContext.Default.CardAppearance) ?? new CardAppearance();
    }

    private static VaultRecord SnapshotRecord(VaultRecord record)
    {
        // Build/Render only consume immutable values, never UI-bound Bitmap objects.
        // Selection/filter changes can therefore continue on the UI thread safely.
        var copy=new VaultRecord
        {
            Uuid=record.Uuid, Group=record.Group, Title=record.Title,
            UserName=record.UserName, Password=record.Password, Url=record.Url,
            AdditionalUrls=(string[])record.AdditionalUrls.Clone(),
            AdditionalUrlCandidateCount=record.AdditionalUrlCandidateCount,
            Notes=record.Notes, IconId=record.IconId, CustomIconUuid=record.CustomIconUuid,
            CustomIconPng=record.CustomIconPng is null?null:(byte[])record.CustomIconPng.Clone(),
            GroupIconId=record.GroupIconId, GroupCustomIconUuid=record.GroupCustomIconUuid,
            AdditionalFields=new Dictionary<string,string>(record.AdditionalFields,StringComparer.OrdinalIgnoreCase),
            HasAttachments=record.HasAttachments
        };
        copy.Selected=record.Selected;
        copy.Visible=record.Visible;
        return copy;
    }

    private Task PresentSceneAsync(SkiaPreviewScene? next,long revision,CancellationToken token,
        PreviewTransitionReason reason=PreviewTransitionReason.Rebuild,bool permitFade=true)
    {
        // Presentation is independent of background rendering. Only references
        // to SKPicture change; every animation frame updates opacity on the UI
        // dispatcher, without affecting zoom, pan, layout or the print pipeline.
        if(token.IsCancellationRequested || revision!=_previewRevision)
        {
            next?.Dispose();
            return Task.CompletedTask;
        }
        bool wasStale=PreviewUpdating;
        bool animate=PreviewTransitionPolicy.ShouldAnimate(_settings,reason,wasStale,permitFade);
        _previewStaleTimer.Stop();
        PreviewUpdating=false;
        _previewCommittedRevision=revision;
        Changed(nameof(PrintReady));
        ResetTransition(keepDominant:animate && next is not null);
        if(next is null)
        {
            _previewScene?.Dispose();
            _previewScene=null;
            Changed(nameof(PreviewScene));
            Changed(nameof(PreviewEmpty));
            return Task.CompletedTask;
        }
        if(!animate || _previewScene is null || ReferenceEquals(_previewScene,next))
        {
            _previewScene?.Dispose();
            _previewScene=next;
            Changed(nameof(PreviewScene));
            Changed(nameof(PreviewEmpty));
            return Task.CompletedTask;
        }
        _previewPreviousScene=_previewScene;
        // An old picture must be completely opaque on the frame the new picture
        // is installed. Otherwise the new content flashes before the first tick.
        PreviewOldOpacity=1;
        Changed(nameof(PreviewPreviousScene));
        _previewScene=next;
        Changed(nameof(PreviewScene));
        Changed(nameof(PreviewEmpty));
        _activeFadeDurationMs=Math.Clamp(_settings.PreviewAnimationDurationMs,50,500);
        _previewFadeClock.Restart();
        _previewFadeTimer.Start();
        return Task.CompletedTask;
    }

    private SkiaPreviewScene? RecordCurrent(IReadOnlyList<PhysicalCard> cards,
        IReadOnlyList<CardPrintSheet> sheets,CardAppearance appearance,bool showSheet,
        int cardIndex,int sheetIndex,bool back,CancellationToken token,long cacheEpoch,
        bool countCacheStats=true)
    {
        token.ThrowIfCancellationRequested();
        if(cards.Count==0)return null;
        var key=$"{cacheEpoch}:{(showSheet?"sheet":"card")}:{(showSheet?sheetIndex:cardIndex)}:{back}";
        if(_previewCache.TryGet(key,out var cached))
        {
            if(countCacheStats)PreviewTelemetry.CacheLookup(true);
            return cached;
        }
        if(countCacheStats)PreviewTelemetry.CacheLookup(false);
        SkiaPreviewScene scene=showSheet
            ? PrintDocumentPipeline.RecordSheet(sheets[sheetIndex],appearance,token)
            : CardSkiaRenderer.Record(cards[cardIndex],appearance,back);
        if(token.IsCancellationRequested)
        {
            scene.Dispose();
            token.ThrowIfCancellationRequested();
        }
        // An obsolete render must never repopulate the cache after invalidation.
        if(Interlocked.Read(ref _previewCacheEpoch)==cacheEpoch)
            _previewCache.Put(key,scene);
        return scene;
    }
    private static int FindCardAnchor(IReadOnlyList<PhysicalCard> cards,Guid uuid,int part,int oldIndex)
    {
        if(cards.Count==0)return 0;
        for(int i=0;i<cards.Count;i++)
            if(cards[i].Front.Record.Uuid==uuid && cards[i].Front.Number==part)return i;
        for(int i=0;i<cards.Count;i++)
            if(cards[i].Front.Record.Uuid==uuid)return i;
        return Math.Clamp(oldIndex,0,cards.Count-1);
    }
    private async Task RebuildCardPreviewAsync()
    {
        long startTimestamp=Stopwatch.GetTimestamp();
        // Defensive fast-path: even a delayed timer callback must not build
        // a fallback card from an unchecked entry.
        if (!VaultRecords.Any(r => r.Selected)) { CloseCardPreview(); return; }
        if (_appearanceBuildRunning) { _appearanceBuildQueued=true; Changed(nameof(PrintReady)); return; }
        _appearanceBuildRunning=true;
        var source=StartPreviewTask(out long revision);
        var token=source.Token;
        try
        {
            var appearance=SnapshotAppearance(_appearance);
            var mapping=CloneMap(_map);
            var chosen=VaultRecords.Where(r=>r.Selected).ToArray();
            bool hasPrintSelection=chosen.Length>0;
            // Do not monopolize Avalonia's dispatcher when thousands of records
            // are selected. Create bounded immutable snapshot batches, yielding
            // between them so pointer, scrollbar and button input can run.
            var selected=new VaultRecord[chosen.Length];
            var budget=Stopwatch.StartNew();
            for(int i=0;i<chosen.Length;i++)
            {
                token.ThrowIfCancellationRequested();
                selected[i]=SnapshotRecord(chosen[i]);
                if(budget.ElapsedMilliseconds>=7)
                {
                    budget.Restart();
                    await Task.Yield();
                }
            }
            var knownIds=VaultRecords.Select(r=>new VaultRecord{Uuid=r.Uuid}).ToArray();
            var anchor=_cardParts.Count>_cardIndex ? _cardParts[_cardIndex].Front : null;
            Guid uuid=anchor?.Record.Uuid ?? (selected.Length>0 ? selected[0].Uuid : Guid.Empty);
            int part=anchor?.Number ?? 1,oldIndex=_cardIndex;
            int oldSheet=_previewSheetIndex;
            bool backSide=_backSide && appearance.DuplexEnabled;
            bool sheetMode=_previewShowSheet;
            var paper=PaperGeometry.Resolve(_settings.PaperFormat,_settings.PrintLandscape);
            bool shortEdge=_settings.PrintShortEdgeFlip;
            int startSlot=_settings.PrintStartSlot-1;
            long cacheEpoch=_previewCacheEpoch;
            var result=await Task.Run(async () =>
            {
                await _previewRenderGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    var parts=CardLayoutEngine.Build(selected,appearance,mapping,knownIds,token);
                    token.ThrowIfCancellationRequested();
                    IReadOnlyList<CardPrintSheet> sheets;
                    try { sheets=PrintDocumentPipeline.Plan(parts,appearance,paper,shortEdge,firstSheetSlot:startSlot); }
                    catch(InvalidOperationException) when(!sheetMode)
                    {
                        // A card may still be previewed when it does not fit
                        // on the selected physical paper. Only export is disabled.
                        sheets=[];
                    }
                    int index=FindCardAnchor(parts,uuid,part,oldIndex);
                    int page=Math.Clamp(oldSheet,0,Math.Max(0,sheets.Count-1));
                    if(sheetMode && parts.Count>0)
                    {
                        // Preserve the same physical entry across changes in pagination.
                        var matching=sheets.Select((s,i)=>(s,i))
                            .FirstOrDefault(t=>t.s.Slots.Any(x=>x.CardIndex==index) &&
                                t.s.Reverse==backSide);
                        page=matching.s is null ? page : matching.i;
                    }
                    var scene=RecordCurrent(parts,sheets,appearance,sheetMode,index,page,
                        backSide,token,cacheEpoch);
                    return new PreviewBuildResult(parts,sheets,index,page,backSide,hasPrintSelection,scene);
                }
                finally { _previewRenderGate.Release(); }
            },token);
            if(token.IsCancellationRequested || revision!=_previewRevision)
            {
                result.Scene?.Dispose();
                return;
            }
            _cardParts.Clear();
            _cardParts.AddRange(result.Cards);
            _previewSheets=result.Sheets;
            _cardIndex=result.CardIndex;
            _previewSheetIndex=result.SheetIndex;
            _backSide=result.BackSide;
            _hasPrintSelection=result.HasPrintSelection;
            UpdatePreviewDisplayWidth();
            _previewError=result.Cards.Count>0 && result.Sheets.Count==0
                ? "Карточка не помещается на выбранном листе без уменьшения. Выберите другой формат." : "";
            NotifyCardPreview();
            await PresentSceneAsync(result.Scene,revision,token);
            if(!token.IsCancellationRequested && revision==_previewRevision)
            {
                PreviewTelemetry.Completed(startTimestamp);
                QueueAdjacentPrefetch();
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch(FileNotFoundException)
        {
            if(revision!=_previewRevision)return;
            _previewError="Не найдены переносимые шрифты. Проверьте комплектность папки fonts/ в дистрибутиве.";
            _previewStaleTimer.Stop();PreviewUpdating=false;
            NotifyCardPreview();
        }
        catch(Exception)
        {
            if(revision!=_previewRevision)return;
            _previewError="Не удалось построить макет. Проверьте размеры бумаги, поля и QR.";
            _previewStaleTimer.Stop();PreviewUpdating=false;
            NotifyCardPreview();
        }
        finally
        {
            if(ReferenceEquals(source,_previewCancellation))_previewCancellation=null;
            source.Dispose();
            _appearanceBuildRunning=false;
            if (_appearanceBuildQueued)
            {
                _appearanceBuildQueued=false;
                _previewTimer.Stop();
                _previewTimer.Start();
                Changed(nameof(PrintReady));
            }
        }
    }

    private async Task RenderExistingCardAsync(
        PreviewTransitionReason reason=PreviewTransitionReason.Rebuild,bool silentZoom=false)
    {
        if(_cardParts.Count==0){NotifyCardPreview();return;}
        long startTimestamp=Stopwatch.GetTimestamp();
        var source=StartPreviewTask(out long revision,announceStale:!silentZoom);
        var token=source.Token;
        try
        {
            var appearance=SnapshotAppearance(_appearance);
            var cardParts=_cardParts.ToArray();
            var sheets=_previewSheets;
            int index=_cardIndex,page=_previewSheetIndex;
            bool back=_backSide,showSheet=_previewShowSheet;
            long cacheEpoch=_previewCacheEpoch;
            var scene=await Task.Run(async () =>
            {
                await _previewRenderGate.WaitAsync(token).ConfigureAwait(false);
                try { return RecordCurrent(cardParts,sheets,appearance,showSheet,index,page,back,token,cacheEpoch); }
                finally { _previewRenderGate.Release(); }
            },token);
            if(token.IsCancellationRequested || revision!=_previewRevision)
            {
                scene?.Dispose();
                return;
            }
            _previewError="";
            NotifyCardPreview();
            await PresentSceneAsync(scene,revision,token,reason,permitFade:!silentZoom);
            if(!token.IsCancellationRequested && revision==_previewRevision)
            {
                PreviewTelemetry.Completed(startTimestamp);
                if(!silentZoom)QueueAdjacentPrefetch();
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch(Exception)
        {
            if(revision!=_previewRevision)return;
            _previewError="Не удалось отрисовать карточку или лист.";
            _previewStaleTimer.Stop();PreviewUpdating=false;
            NotifyCardPreview();
        }
        finally
        {
            if(ReferenceEquals(source,_previewCancellation))_previewCancellation=null;
            source.Dispose();
        }
    }

    private void QueueAdjacentPrefetch()
    {
        _prefetchCancellation?.Cancel();
        if(_cardParts.Count==0)return;
        var source=new CancellationTokenSource();
        _prefetchCancellation=source;
        var token=source.Token;
        var appearance=SnapshotAppearance(_appearance);
        var cards=_cardParts.ToArray();
        var sheets=_previewSheets;
        var showSheet=_previewShowSheet;
        int index=showSheet?_previewSheetIndex:_cardIndex;
        bool back=_backSide;
        long epoch=_previewCacheEpoch;
        _ = Task.Run(async () =>
        {
            try
            {
                await _prefetchGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    int count=showSheet?sheets.Count:cards.Length;
                    foreach(var neighbour in new[]{index+1,index-1})
                    {
                        token.ThrowIfCancellationRequested();
                        if(neighbour<0 || neighbour>=count)continue;
                        using var picture=RecordCurrent(cards,sheets,appearance,showSheet,
                            showSheet?0:neighbour,showSheet?neighbour:0,
                            showSheet?sheets[neighbour].Reverse:back,token,epoch,countCacheStats:false);
                    }
                }
                finally { _prefetchGate.Release(); }
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested) { }
            catch(Exception) { /* Speculative work is best-effort and never logged. */ }
            finally
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if(ReferenceEquals(_prefetchCancellation,source))_prefetchCancellation=null;
                    source.Dispose();
                });
            }
        });
    }

    public (CardAppearance Appearance,IReadOnlyList<CardPrintSheet> Sheets)? SnapshotPrintableJob()
    {
        if(!PrintReady)return null;
        return (SnapshotAppearance(_appearance),_previewSheets);
    }
    public void SetExporting(bool exporting)
    {
        _previewExporting=exporting;
        Changed(nameof(PrintReady));
    }
}
