using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using KeePassXCCardPrint.Models;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.ViewModels;

public sealed class MappingOption : INotifyPropertyChanged
{
    public string Code { get; }
    private readonly string _kind;
    private string _label;
    public string Label { get => _label; private set { _label = value; PropertyChanged?.Invoke(this,new(nameof(Label))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public MappingOption(string kind, string code, string language) { _kind=kind;Code=code; _label = MappingLexicon.OptionLabel(kind,code, language); }
    public void Relocalize(string language) => Label = MappingLexicon.OptionLabel(_kind,Code, language);
}

public sealed class FieldMappingRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name=null) => PropertyChanged?.Invoke(this,new(name));
    private int _position;
    private string _label="", _role="", _section="", _visibility="", _heading="", _fit="", _grouping="", _displayGroup="";
    private int _order;
    private bool _hasDraftError;
    public bool HasDraftError { get=>_hasDraftError; set { if(_hasDraftError==value)return; _hasDraftError=value;Changed();Changed(nameof(PositionText)); } }
    public string PositionText => _hasDraftError ? $"! {Position}" : Position.ToString(CultureInfo.InvariantCulture);
    public string Field { get; }
    public int Position { get=>_position; private set { if(_position==value)return; _position=value;Changed();Changed(nameof(PositionText)); } }
    public string Label { get=>_label; private set { if(_label==value)return; _label=value;Changed(); } }
    public string Role { get=>_role; private set { if(_role==value)return; _role=value;Changed(); } }
    public string Section { get=>_section; private set { if(_section==value)return; _section=value;Changed(); } }
    public int Order { get=>_order; private set { if(_order==value)return; _order=value;Changed(); } }
    public string Visibility { get=>_visibility; private set { if(_visibility==value)return; _visibility=value;Changed(); } }
    public string Heading { get=>_heading; private set { if(_heading==value)return; _heading=value;Changed(); } }
    public string Fit { get=>_fit; private set { if(_fit==value)return; _fit=value;Changed(); } }
    public string Grouping { get=>_grouping; private set { if(_grouping==value)return; _grouping=value;Changed(); } }
    public string DisplayGroup { get=>_displayGroup; private set { if(_displayGroup==value)return; _displayGroup=value;Changed(); } }
    public FieldMappingRow(string field, FieldRule r, int pos, string language)
    { Field=field; Update(r,pos,language); }
    public void Update(FieldRule r, int pos, string language)
    {
        Position=pos; Label=r.Label; Order=r.Order;
        Role=MappingLexicon.OptionLabel("role",r.Role,language);
        Section=MappingLexicon.OptionLabel("section",r.Section,language);
        Visibility=MappingLexicon.OptionLabel("visibility",r.Visibility,language);
        Heading=MappingLexicon.OptionLabel("heading",r.ShowLabel?"on":"off",language);
        Fit=MappingLexicon.OptionLabel("fit",r.Fit,language);
        Grouping=MappingLexicon.OptionLabel("group",r.GroupMode,language)+(r.GroupSize>0?$" · {r.GroupSize}":"");
        DisplayGroup=r.Role switch { "codes"=>"Коды", "bitlocker"=>"BitLocker", "totp"=>"TOTP", _=>"—" };
    }
}

// Kept only for incomplete numeric input. It never reaches the card renderer or disk.
public sealed record MappingFieldDraft(
    string Label, string Order, string SizePct, string TrackingPt, string GroupSize, string GroupSpaces,
    string Role, string Section, string Visibility, string Heading, string UrlMode, string UrlSource,
    string Fit, string Weight, string Group, bool PhoneFormat);

/// <summary>Field editor: valid changes update the in-memory map, never the saved JSON.</summary>
public sealed class MappingEditor : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName]string? name=null) => PropertyChanged?.Invoke(this,new(name));
    private string _field = "";
    private bool _enabled;
    public string Field { get=>_field; private set { _field=value; Changed(); } }
    public bool Enabled { get=>_enabled; private set { _enabled=value; Changed(); } }
    private string _label="";
    private string _order="500";
    private string _size="100";
    private string _tracking="";
    private string _groupSize="0";
    private string _groupSpaces="0";
    public string Label { get=>_label;set { _label=value??"";Changed(); } }
    public string Order { get=>_order;set { _order=value??"";Changed(); } }
    public string SizePct { get=>_size;set { _size=value??"";Changed(); } }
    public string TrackingPt { get=>_tracking;set { _tracking=value??"";Changed(); } }
    public string GroupSize { get=>_groupSize;set { _groupSize=value??"";Changed(); } }
    public string GroupSpaces { get=>_groupSpaces;set { _groupSpaces=value??"";Changed(); } }
    public ObservableCollection<MappingOption> Roles { get; } = new();
    public ObservableCollection<MappingOption> Sections { get; } = new();
    public ObservableCollection<MappingOption> Visibilities { get; } = new();
    public ObservableCollection<MappingOption> Headings { get; } = new();
    public ObservableCollection<MappingOption> UrlModes { get; } = new();
    public ObservableCollection<MappingOption> UrlSources { get; } = new();
    public ObservableCollection<MappingOption> Fits { get; } = new();
    public ObservableCollection<MappingOption> Weights { get; } = new();
    public ObservableCollection<MappingOption> Groups { get; } = new();
    private MappingOption? _role, _section, _visibility, _heading, _url, _urlSource, _fit, _weight, _group;
    public MappingOption? Role { get=>_role;set { _role=value;Changed(); } }
    public MappingOption? Section { get=>_section;set { _section=value;Changed(); } }
    public MappingOption? Visibility { get=>_visibility;set { _visibility=value;Changed(); } }
    public MappingOption? Heading { get=>_heading;set { _heading=value;Changed(); } }
    public MappingOption? UrlMode { get=>_url;set { _url=value;Changed(); } }
    public MappingOption? UrlSource { get=>_urlSource;set { _urlSource=value;Changed(); } }
    public bool IsUrlField => Field.Equals("URL",StringComparison.OrdinalIgnoreCase);
    public bool IsUsernameField => Field.Equals("USERNAME",StringComparison.OrdinalIgnoreCase);
    private bool _phoneFormat;
    public bool PhoneFormat { get => _phoneFormat; set { if (_phoneFormat == value) return; _phoneFormat = value; Changed(); } }
    public string UrlSourceTitle => MappingLexicon.Text("UrlSource",_language);
    private string _urlPreviewStatus = "";
    public string UrlPreviewStatus { get => _urlPreviewStatus; private set { if (_urlPreviewStatus == value) return; _urlPreviewStatus = value; Changed(); } }
    public void SetUrlPreviewStatus(string value) => UrlPreviewStatus = value;
    public MappingOption? Fit { get=>_fit;set { _fit=value;Changed(); } }
    public MappingOption? Weight { get=>_weight;set { _weight=value;Changed(); } }
    public MappingOption? Group { get=>_group;set { _group=value;Changed(); } }
    private string _validationMessage="";
    public string ValidationMessage { get=>_validationMessage; private set { if(_validationMessage==value)return; _validationMessage=value; Changed(); Changed(nameof(HasValidationError)); } }
    public bool HasValidationError => !string.IsNullOrEmpty(_validationMessage);
    private bool _orderInvalid,_sizeInvalid,_trackingInvalid,_groupSizeInvalid,_groupSpacesInvalid;
    public bool OrderInvalid { get=>_orderInvalid; private set { if(_orderInvalid==value)return; _orderInvalid=value;Changed(); } }
    public bool SizePctInvalid { get=>_sizeInvalid; private set { if(_sizeInvalid==value)return; _sizeInvalid=value;Changed(); } }
    public bool TrackingPtInvalid { get=>_trackingInvalid; private set { if(_trackingInvalid==value)return; _trackingInvalid=value;Changed(); } }
    public bool GroupSizeInvalid { get=>_groupSizeInvalid; private set { if(_groupSizeInvalid==value)return; _groupSizeInvalid=value;Changed(); } }
    public bool GroupSpacesInvalid { get=>_groupSpacesInvalid; private set { if(_groupSpacesInvalid==value)return; _groupSpacesInvalid=value;Changed(); } }
    private static bool GoodInt(string text,int lo,int hi) => int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var x) && x>=lo && x<=hi;
    private static bool GoodDouble(string text,double lo,double hi) => double.TryParse(text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var x) && double.IsFinite(x) && x>=lo && x<=hi;
    public void Validate(string? error)
    {
        OrderInvalid=!GoodInt(Order,-9999,9999);
        SizePctInvalid=!GoodInt(SizePct,65,160);
        TrackingPtInvalid=!string.IsNullOrWhiteSpace(TrackingPt) && !GoodDouble(TrackingPt,-.5,1.5);
        GroupSizeInvalid=!GoodInt(GroupSize,0,16) || GroupSize.Trim()=="1";
        GroupSpacesInvalid=!GoodDouble(GroupSpaces,0,2) ||
            (GoodDouble(GroupSpaces,0,2) && double.TryParse(GroupSpaces.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var sp) && sp>0 && sp<.1);
        ValidationMessage=error??"";
    }
    public MappingFieldDraft CaptureDraft() => new(Label,Order,SizePct,TrackingPt,GroupSize,GroupSpaces,
        Role?.Code??"",Section?.Code??"",Visibility?.Code??"",Heading?.Code??"",UrlMode?.Code??"",UrlSource?.Code??"",
        Fit?.Code??"",Weight?.Code??"",Group?.Code??"",PhoneFormat);
    public void RestoreDraft(MappingFieldDraft draft)
    {
        Label=draft.Label; Order=draft.Order; SizePct=draft.SizePct; TrackingPt=draft.TrackingPt;
        GroupSize=draft.GroupSize; GroupSpaces=draft.GroupSpaces;
        Role=Select(Roles,draft.Role); Section=Select(Sections,draft.Section);
        Visibility=Select(Visibilities,draft.Visibility); Heading=Select(Headings,draft.Heading);
        UrlMode=Select(UrlModes,draft.UrlMode); UrlSource=Select(UrlSources,draft.UrlSource); Fit=Select(Fits,draft.Fit);
        Weight=Select(Weights,draft.Weight); Group=Select(Groups,draft.Group); PhoneFormat=draft.PhoneFormat;
    }
    private string _language;
    public string PhoneCaption => UiText.Tr("Логин — телефон",_language);
    public string PhoneFormatCaption => UiText.Tr("Форматировать номер",_language);
    public string DisplayTitle => MappingLexicon.Text("Display",_language);
    public string GroupTitleTitle => MappingLexicon.Text("GroupTitle",_language);
    public string LabelTitle => MappingLexicon.Text("Label",_language);
    public string PriorityTitle => MappingLexicon.Text("Priority",_language);
    public string VisibleTitle => MappingLexicon.Text("Visible",_language);
    public string ShowHeadingTitle => MappingLexicon.Text("ShowHeading",_language);
    public string UrlTitle => MappingLexicon.Text("Url",_language);
    public string FitTitle => MappingLexicon.Text("Fit",_language);
    public string WeightTitle => MappingLexicon.Text("Weight",_language);
    public string SizeTitle => MappingLexicon.Text("Size",_language);
    public string TrackingTitle => MappingLexicon.Text("Tracking",_language);
    public string GroupTitle => MappingLexicon.Text("Group",_language);
    public string GroupSizeTitle => MappingLexicon.Text("GroupSize",_language);
    public string GroupSpacesTitle => MappingLexicon.Text("GroupSpaces",_language);
    public string DisplayHelp => MappingLexicon.Help("Display",_language);
    public string GroupTitleHelp => MappingLexicon.Help("GroupTitle",_language);
    public string LabelHelp => MappingLexicon.Help("Label",_language);
    public string PriorityHelp => MappingLexicon.Help("Priority",_language);
    public string VisibleHelp => MappingLexicon.Help("Visible",_language);
    public string ShowHeadingHelp => MappingLexicon.Help("ShowHeading",_language);
    public string UrlHelp => MappingLexicon.Help("Url",_language);
    public string FitHelp => MappingLexicon.Help("Fit",_language);
    public string WeightHelp => MappingLexicon.Help("Weight",_language);
    public string SizeHelp => MappingLexicon.Help("Size",_language);
    public string TrackingHelp => MappingLexicon.Help("Tracking",_language);
    public string GroupHelp => MappingLexicon.Help("Group",_language);
    public string GroupSizeHelp => MappingLexicon.Help("GroupSize",_language);
    public string GroupSpacesHelp => MappingLexicon.Help("GroupSpaces",_language);
    public MappingEditor(string language)
    {
        _language=language;
        Fill(Roles,"role",["text","secret","codes","bitlocker","totp","multiline"]);
        Fill(Sections,"section",["account","auth","recovery","extra"]);
        Fill(Visibilities,"visibility",["nonempty","always","hidden"]);
        Fill(Headings,"heading",["on","off"]);
        Fill(UrlModes,"url",["full","domain","smart"]);
        Fill(UrlSources,"urlsource",["main","fallback","extra"]);
        Fill(Fits,"fit",["wrap","shrink","split","smart"]);
        Fill(Weights,"weight",["inherit","200","300","400","500","600","700","800"]);
        Fill(Groups,"group",["inherit","on","off","auto"]);
    }
    private void Fill(ObservableCollection<MappingOption> collection,string kind, string[] codes)
    { foreach(var code in codes) collection.Add(new MappingOption(kind,code,_language)); }
    public void Relocalize(string language)
    {
        _language=language;
        foreach(var key in new[]{nameof(DisplayTitle),nameof(GroupTitleTitle),nameof(LabelTitle),nameof(PriorityTitle),nameof(VisibleTitle),nameof(ShowHeadingTitle),nameof(UrlTitle),nameof(FitTitle),nameof(WeightTitle),nameof(SizeTitle),nameof(TrackingTitle),nameof(GroupTitle),nameof(GroupSizeTitle),nameof(GroupSpacesTitle),nameof(UrlSourceTitle),nameof(PhoneCaption),nameof(PhoneFormatCaption)})Changed(key);
        foreach(var key in new[]{nameof(DisplayHelp),nameof(GroupTitleHelp),nameof(LabelHelp),nameof(PriorityHelp),nameof(VisibleHelp),nameof(ShowHeadingHelp),nameof(UrlHelp),nameof(FitHelp),nameof(WeightHelp),nameof(SizeHelp),nameof(TrackingHelp),nameof(GroupHelp),nameof(GroupSizeHelp),nameof(GroupSpacesHelp)})Changed(key);
        foreach(var items in new[]{Roles,Sections,Visibilities,Headings,UrlModes,UrlSources,Fits,Weights,Groups})
            foreach(var option in items) option.Relocalize(language);
    }
    private static MappingOption Select(ObservableCollection<MappingOption> options,string code) =>
        options.FirstOrDefault(o=>o.Code==code) ?? options[0];
    public void Load(string? field, FieldRule? r)
    {
        Enabled=r is not null; Field=field??""; Changed(nameof(IsUrlField)); Changed(nameof(IsUsernameField));
        r ??= new();
        Label=r.Label; Order=r.Order.ToString(CultureInfo.InvariantCulture);
        SizePct=r.ValueSizePct.ToString(CultureInfo.InvariantCulture);
        TrackingPt=r.ValueTrackingPt?.ToString("0.###", CultureInfo.InvariantCulture)??"";
        GroupSize=r.GroupSize.ToString(CultureInfo.InvariantCulture);
        GroupSpaces=r.GroupSpaces.ToString("0.###", CultureInfo.InvariantCulture);
        Role=Select(Roles,r.Role); Section=Select(Sections,r.Section);
        Visibility=Select(Visibilities,r.Visibility); Heading=Select(Headings,r.ShowLabel?"on":"off");
        UrlMode=Select(UrlModes,r.UrlMode); UrlSource=Select(UrlSources,r.UrlSource); Fit=Select(Fits,r.Fit);
        Weight=Select(Weights,r.ValueWeight=="inherit"?"inherit":FontWeights.Normalize(r.ValueWeight)); Group=Select(Groups,r.GroupMode); PhoneFormat=r.PhoneFormat;
    }
    private static double ParseDouble(string text,string label,double min,double max)
    {
        if (!double.TryParse(text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var value) ||
            !double.IsFinite(value) || value<min || value>max)
            throw new ArgumentException($"{label}: {min}–{max}");
        return value;
    }
    private static int ParseInt(string text,string label,int min,int max)
    {
        if (!int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var value) || value<min || value>max)
            throw new ArgumentException($"{label}: {min}–{max}");
        return value;
    }
    public FieldRule ToRule()
    {
        if (!Enabled || Role is null || Section is null || Visibility is null || Heading is null || UrlMode is null || UrlSource is null || Fit is null || Weight is null || Group is null)
            throw new ArgumentException("Выберите поле и параметры.");
        var size=ParseInt(SizePct,"Размер значения",65,160);
        var groupSize=ParseInt(GroupSize,"Символов в группе",0,16);
        if (groupSize==1) throw new ArgumentException("Символов в группе: 0 или 2–16");
        var spaces=ParseDouble(GroupSpaces,"Отступ между группами",0,2);
        if (spaces>0 && spaces<0.1) throw new ArgumentException("Отступ: 0 или 0,1–2,0");
        return new FieldRule {
            Label=Label.Trim()[..Math.Min(120,Label.Trim().Length)],
            Role=Role.Code, Section=Section.Code,
            Order=ParseInt(Order,"Приоритет",-9999,9999),
            Visibility=Visibility.Code,ShowLabel=Heading.Code=="on",
            UrlMode=UrlMode.Code,UrlSource=UrlSource.Code,PhoneFormat=IsUsernameField && PhoneFormat,Fit=Fit.Code,ValueWeight=Weight.Code,
            ValueSizePct=size,
            ValueTrackingPt=string.IsNullOrWhiteSpace(TrackingPt)?null:ParseDouble(TrackingPt,"Интервал букв",-.5,1.5),
            GroupMode=Group.Code,GroupSize=groupSize,GroupSpaces=spaces
        };
    }
}

public sealed partial class ShellViewModel
{
    private bool _mappingScopeCurrent;
    private bool _mappingAllColumns;
    private FieldMappingRow? _selectedMappingRow;
    private string _mappingMessage="";
    private bool _mappingInitialized;
    private bool _mappingLoadingEditor;
    private bool _mappingDirty;
    private bool _mappingSaveFailed;
    private Dictionary<string, FieldRule> _savedMapping = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MappingFieldDraft> _invalidMappingDrafts = new(StringComparer.OrdinalIgnoreCase);
    private string ML(string key) => MappingLexicon.Text(key,_settings.Language);
    public ObservableCollection<FieldMappingRow> FieldRows { get; } = new();
    public MappingEditor MappingEditor { get; private set; } = null!;
    public bool MappingScopeCurrent { get=>_mappingScopeCurrent; set { if(_mappingScopeCurrent==value)return; _mappingScopeCurrent=value; _settings.MappingScopeCurrent=value; Changed(); RebuildMapping(); PreferencesChanged?.Invoke(this,EventArgs.Empty); } }
    public bool MappingAllColumns { get=>_mappingAllColumns; set { if(_mappingAllColumns==value)return; _mappingAllColumns=value; _settings.MappingAllColumns=value; Changed(); Changed(nameof(MappingCompactVisible)); Changed(nameof(MappingDetailedVisible)); PreferencesChanged?.Invoke(this,EventArgs.Empty); } }
    public bool MappingCompactVisible => !_mappingAllColumns;
    public bool MappingDetailedVisible => _mappingAllColumns;
    public FieldMappingRow? SelectedMappingRow
    {
        get=>_selectedMappingRow;
        set
        {
            if(ReferenceEquals(_selectedMappingRow,value))return;
            _selectedMappingRow=value; Changed();
            _mappingLoadingEditor=true;
            try
            {
                MappingEditor.Load(value?.Field,value is null ? null : GetRule(value.Field));
                if(value is not null && _invalidMappingDrafts.TryGetValue(value.Field,out var draft))
                    MappingEditor.RestoreDraft(draft);
                CheckCurrentEditor();
            }
            finally { _mappingLoadingEditor=false; }
            Changed(nameof(MappingSelectedLabel));Changed(nameof(MappingFieldHeading));Changed(nameof(MappingCanEdit));
            UpdateUrlPreviewStatus();
        }
    }
    public bool MappingCanEdit => _selectedMappingRow is not null;
    public string MappingSelectedLabel => _selectedMappingRow?.Field ?? Localizer.Text(_settings.Language,"NotSelected");
    public string MappingFieldHeading => _selectedMappingRow is null
        ? Localizer.Text(_settings.Language,"FieldOptions")
        : $"{Localizer.Text(_settings.Language,"FieldOptions")} {_selectedMappingRow.Field}";
    public string MappingMessage { get=>_mappingMessage;private set { _mappingMessage=value;Changed(); } }
    public bool MappingHasPendingChanges => _mappingDirty || _invalidMappingDrafts.Count>0;
    public bool MappingCanSave => _mappingDirty && _invalidMappingDrafts.Count==0;
    public string MappingScopeDescription { get; private set; } = "";
    public string MappingScopeHelpText => ML("ScopeHelp");
    public string MappingFilterHelpText => ML("FilterHelp");
    public string MappingHeaderText => ML("Header");
    public string MappingToolbarTitle => ML("Toolbar");
    public string MappingFieldsHeading => ML("FieldsList");
    public string MappingLabelText => ML("Label");
    public string MappingPriorityText => ML("Priority");
    public string MappingVisibleText => ML("Visible");
    public string MappingShowHeadingText => ML("ShowHeading");
    public string MappingUrlText => ML("Url");
    public string MappingFitText => ML("Fit");
    public string MappingWeightText => ML("Weight");
    public string MappingSizeText => ML("Size");
    public string MappingTrackingText => ML("Tracking");
    public string MappingGroupText => ML("Group");
    public string MappingGroupSizeText => ML("GroupSize");
    public string MappingGroupSpacesText => ML("GroupSpaces");
    public string MappingResetText => ML("Reset");
    public string MappingSaveText => ML("Save");
    public string MappingDomainsText => ML("Domains");
    public string MappingDisplayText => ML("Display");
    public string MappingGroupTitleText => ML("GroupTitle");
    public string MappingFieldTitleText => ML("FieldTitle");
    public string MappingCardTitleText => ML("CardTitle");

    private void InitializeMapping()
    {
        _mappingScopeCurrent=_settings.MappingScopeCurrent;
        _mappingAllColumns=_settings.MappingAllColumns;
        MappingEditor=new MappingEditor(_settings.Language);
        _savedMapping=CloneMap(_map);
        MappingEditor.PropertyChanged += OnMappingEditorChanged;
        RebuildMapping();
        UpdateMappingStatus();
    }
    public void RelocalizeMapping()
    {
        if (!_mappingInitialized) return;
        MappingEditor.Relocalize(_settings.Language);
        Changed(nameof(MappingFieldHeading));
        foreach(var key in new[]{nameof(MappingScopeHelpText),nameof(MappingFilterHelpText),nameof(MappingHeaderText),nameof(MappingToolbarTitle),nameof(MappingFieldsHeading),nameof(MappingLabelText),nameof(MappingPriorityText),nameof(MappingVisibleText),nameof(MappingShowHeadingText),nameof(MappingUrlText),nameof(MappingFitText),nameof(MappingWeightText),nameof(MappingSizeText),nameof(MappingTrackingText),nameof(MappingGroupText),nameof(MappingGroupSizeText),nameof(MappingGroupSpacesText),nameof(MappingResetText),nameof(MappingSaveText),nameof(MappingDomainsText),nameof(MappingDisplayText),nameof(MappingGroupTitleText),nameof(MappingFieldTitleText),nameof(MappingCardTitleText)}) Changed(key);
        RebuildMapping();
        UpdateMappingStatus();
    }
    private FieldRule GetRule(string key) => _map.TryGetValue(key,out var r) ? r : FieldMappingDefaults.Infer(key);
    private VaultRecord? CurrentMappingRecord() => VaultRecords.FirstOrDefault(r=>r.Selected) ?? VaultRecords.FirstOrDefault(r=>r.Visible);
    public void RebuildMapping()
    {
        if(MappingEditor is null)return;
        var old=_selectedMappingRow?.Field;
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"GROUP","USERNAME","PASSWORD","URL","NOTES"};
        var record=MappingScopeCurrent ? CurrentMappingRecord(): null;
        if(record is not null)
        {
            names.Clear(); names.Add("GROUP");
            if (!string.IsNullOrWhiteSpace(record.UserName)) names.Add("USERNAME");
            if (!string.IsNullOrWhiteSpace(record.Password)) names.Add("PASSWORD");
            if (!string.IsNullOrWhiteSpace(record.Url) || record.AdditionalUrls.Length > 0) names.Add("URL");
            if (!string.IsNullOrWhiteSpace(record.Notes)) names.Add("NOTES");
            foreach(var k in record.AdditionalFields.Keys)
                if(!UrlSourceSelector.IsAdditionalUrlAttribute(k)) names.Add(k);
            MappingScopeDescription=string.Format(MappingLexicon.Text("CurrentCard",_settings.Language),record.Title);
        }
        else
        {
            foreach(var r in VaultRecords)
                foreach(var k in r.AdditionalFields.Keys)
                    if(!UrlSourceSelector.IsAdditionalUrlAttribute(k)) names.Add(k);
            foreach(var k in _map.Keys)
                if(!UrlSourceSelector.IsAdditionalUrlAttribute(k)) names.Add(k);
            MappingScopeDescription=MappingLexicon.Text("AllKnown",_settings.Language);
        }
        Changed(nameof(MappingScopeDescription));
        var list=names.Select(n=>(Name:n,Rule:GetRule(n)))
            .Where(x=>record is null || x.Rule.Visibility!="hidden")
            .OrderBy(x=>FieldMappingDefaults.SectionRank(x.Rule.Section))
            .ThenBy(x=>x.Rule.Order).ThenBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ToList();
        _selectedMappingRow=null;
        FieldRows.Clear();
        int position=0;
        foreach(var x in list)
        {
            var row=new FieldMappingRow(x.Name,x.Rule,++position,_settings.Language);
            row.HasDraftError=_invalidMappingDrafts.ContainsKey(x.Name);
            FieldRows.Add(row);
        }
        SelectedMappingRow=FieldRows.FirstOrDefault(x=>string.Equals(x.Field,old,StringComparison.OrdinalIgnoreCase)) ?? FieldRows.FirstOrDefault();
        _mappingInitialized=true;
    }
    private void RefreshMappingRows()
    {
        // Update existing row objects instead of replacing the editor during typing.
        // Moving a row preserves the ListBox selection and TextBox caret.
        var ordered=FieldRows.OrderBy(x=>FieldMappingDefaults.SectionRank(GetRule(x.Field).Section))
            .ThenBy(x=>GetRule(x.Field).Order).ThenBy(x=>x.Field,StringComparer.OrdinalIgnoreCase).ToArray();
        for(int i=0;i<ordered.Length;i++)
        {
            var row=ordered[i];
            int index=FieldRows.IndexOf(row);
            if(index!=i) FieldRows.Move(index,i);
            row.Update(GetRule(row.Field),i+1,_settings.Language);
            row.HasDraftError=_invalidMappingDrafts.ContainsKey(row.Field);
        }
    }
    private void CheckCurrentEditor()
    {
        if (_selectedMappingRow is null) { MappingEditor.Validate(null); return; }
        try { MappingEditor.ToRule(); MappingEditor.Validate(null); }
        catch(ArgumentException e) { MappingEditor.Validate(e.Message); }
    }
    private void UpdateUrlPreviewStatus()
    {
        if (MappingEditor is null) return;
        var record = VaultRecords.FirstOrDefault(x => x.Selected) ??
                     VaultRecords.FirstOrDefault(x => x.Visible);
        var rule = GetRule("URL");
        MappingEditor.SetUrlPreviewStatus(UrlSourceSelector.ExplainForMapping(record,
            rule.UrlSource, rule.Visibility, _appearance.PrintUrl,
            Localizer.ResolveLanguage(_settings.Language)));
    }

    private static bool RuleEquals(FieldRule a, FieldRule b) =>
        a.Label==b.Label && a.Role==b.Role && a.Section==b.Section && a.Order==b.Order &&
        a.Visibility==b.Visibility && a.ShowLabel==b.ShowLabel && a.UrlMode==b.UrlMode && a.UrlSource==b.UrlSource && a.PhoneFormat==b.PhoneFormat &&
        a.Fit==b.Fit && a.ValueWeight==b.ValueWeight && a.ValueSizePct==b.ValueSizePct &&
        a.ValueTrackingPt==b.ValueTrackingPt && a.GroupMode==b.GroupMode &&
        a.GroupSize==b.GroupSize && a.GroupSpaces==b.GroupSpaces;
    private static FieldRule CloneRule(FieldRule x) => new()
    {
        Label=x.Label,Role=x.Role,Section=x.Section,Order=x.Order,Visibility=x.Visibility,
        ShowLabel=x.ShowLabel,UrlMode=x.UrlMode,UrlSource=x.UrlSource,PhoneFormat=x.PhoneFormat,Fit=x.Fit,ValueWeight=x.ValueWeight,
        ValueSizePct=x.ValueSizePct,ValueTrackingPt=x.ValueTrackingPt,GroupMode=x.GroupMode,
        GroupSize=x.GroupSize,GroupSpaces=x.GroupSpaces
    };
    private static Dictionary<string, FieldRule> CloneMap(Dictionary<string,FieldRule> source) =>
        source.ToDictionary(x=>x.Key,x=>CloneRule(x.Value),StringComparer.OrdinalIgnoreCase);
    private static bool MapEquals(Dictionary<string,FieldRule> left,Dictionary<string,FieldRule> right) =>
        left.Count==right.Count && left.All(x=>right.TryGetValue(x.Key,out var rule) && RuleEquals(x.Value,rule));
    private void UpdateMappingStatus()
    {
        _mappingDirty=!MapEquals(_map,_savedMapping);
        MappingMessage=_invalidMappingDrafts.Count>0 ? ML("InvalidDraft") :
            _mappingSaveFailed ? ML("SaveError") : _mappingDirty ? ML("Dirty") : "";
        Changed(nameof(MappingCanSave)); Changed(nameof(MappingHasPendingChanges));
    }
    private void OnMappingEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if(_mappingLoadingEditor || _selectedMappingRow is null) return;
        if(e.PropertyName is not (nameof(MappingEditor.Label) or nameof(MappingEditor.Order) or
            nameof(MappingEditor.SizePct) or nameof(MappingEditor.TrackingPt) or
            nameof(MappingEditor.GroupSize) or nameof(MappingEditor.GroupSpaces) or
            nameof(MappingEditor.Role) or nameof(MappingEditor.Section) or nameof(MappingEditor.Visibility) or
            nameof(MappingEditor.Heading) or nameof(MappingEditor.UrlMode) or nameof(MappingEditor.UrlSource) or nameof(MappingEditor.PhoneFormat) or nameof(MappingEditor.Fit) or
            nameof(MappingEditor.Weight) or nameof(MappingEditor.Group)))return;
        var field=_selectedMappingRow.Field;
        try
        {
            var rule=MappingEditor.ToRule();
            MappingEditor.Validate(null);
            _invalidMappingDrafts.Remove(field);
            // A field inferred from defaults must not become an explicit override
            // when the editor contains the same values as before.
            if(!_savedMapping.ContainsKey(field) && RuleEquals(rule,FieldMappingDefaults.Infer(field)))
                _map.Remove(field);
            else
                _map[field.Trim().ToLowerInvariant()]=rule;
            RefreshMappingRows();
            ScheduleCardPreview();
        }
        catch(ArgumentException ex)
        {
            MappingEditor.Validate(ex.Message);
            _invalidMappingDrafts[field]=MappingEditor.CaptureDraft();
            _selectedMappingRow.HasDraftError=true;
        }
        _mappingSaveFailed=false;
        UpdateMappingStatus();
        UpdateUrlPreviewStatus();
    }
    public void ResetMapping()
    {
        if(SelectedMappingRow is null)return;
        var field=SelectedMappingRow.Field;
        _invalidMappingDrafts.Remove(field);
        _map.Remove(field);
        _mappingLoadingEditor=true;
        try { MappingEditor.Load(field,GetRule(field)); MappingEditor.Validate(null); }
        finally { _mappingLoadingEditor=false; }
        RefreshMappingRows();
        ScheduleCardPreview();
        _mappingSaveFailed=false;
        UpdateMappingStatus();
    }
    // Explicit save only; callers must not silently persist mapping when leaving the app.
    public bool SaveMapping()
    {
        if(_invalidMappingDrafts.Count>0)
        {
            UpdateMappingStatus();
            return false;
        }
        if(!_mappingDirty) { UpdateMappingStatus(); return true; }
        try
        {
            FieldMappingStore.Save(_map);
            _savedMapping=CloneMap(_map);
            _mappingSaveFailed=false;
            UpdateMappingStatus();
            return true;
        }
        catch(Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _mappingSaveFailed=true;
            UpdateMappingStatus();
            return false;
        }
    }
}
