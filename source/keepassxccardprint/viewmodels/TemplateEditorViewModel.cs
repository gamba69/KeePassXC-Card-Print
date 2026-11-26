using System.Collections.ObjectModel;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.ViewModels;

public sealed partial class ShellViewModel
{
    public ObservableCollection<TemplateEditorSection> TemplateEditorSections { get; } = new();
    public ObservableCollection<TemplateEditorGroup> TemplateEditorGroups { get; } = new();
    private TemplateEditorSection? _selectedTemplateEditorSection;
    public TemplateEditorSection? SelectedTemplateEditorSection
    {
        get => _selectedTemplateEditorSection;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedTemplateEditorSection)) return;
            _selectedTemplateEditorSection = value;
            Changed();
            Changed(nameof(TemplateMappingVisible));
            Changed(nameof(TemplateAppearanceRow));
            Changed(nameof(TemplateAppearanceRowSpan));
            Changed(nameof(TemplateAppearanceVisible));
            Changed(nameof(TemplateAppearanceMaxHeight));
            RebuildTemplateEditorGroups();
            HighlightTemplateGroup("");
        }
    }
    public bool TemplateMappingVisible => _selectedTemplateEditorSection?.Id == "mapping";
    private bool _mappingRestrictionsExpanded;
    private double _templateViewportHeight = 300;
    public bool MappingRestrictionsExpanded
    {
        get => _mappingRestrictionsExpanded;
        set
        {
            if (_mappingRestrictionsExpanded == value) return;
            _mappingRestrictionsExpanded = value;
            Changed();
            Changed(nameof(TemplateAppearanceVisible));
        }
    }
    public string MappingRestrictionsCaption => TemplateEditorLayout.L(_settings.Language,
        "Общие ограничения печати", "Загальні обмеження друку", "Global print restrictions");
    public int TemplateAppearanceRow => TemplateMappingVisible ? 2 : 0;
    public int TemplateAppearanceRowSpan => TemplateMappingVisible ? 1 : 3;
    public bool TemplateAppearanceVisible => !TemplateMappingVisible || MappingRestrictionsExpanded;
    public double TemplateAppearanceMaxHeight => TemplateMappingVisible
        ? AdaptiveViewport.RestrictionsHeight(_templateViewportHeight) : double.PositiveInfinity;
    internal void SetTemplateViewportHeight(double height)
    {
        if (height <= 0 || Math.Abs(_templateViewportHeight-height)<.5) return;
        _templateViewportHeight = height;
        Changed(nameof(TemplateAppearanceMaxHeight));
    }
    public bool TemplateEditorActive => ActiveTab == 1;

    private void RelocalizeTemplateEditor()
    {
        var id = _selectedTemplateEditorSection?.Id ?? "general";
        Changed(nameof(MappingRestrictionsCaption));
        TemplateEditorSections.Clear();
        foreach (var section in TemplateEditorLayout.Sections)
            TemplateEditorSections.Add(new(section.Id, TemplateEditorLayout.L(_settings.Language, section.Ru, section.Uk, section.En)));
        SelectedTemplateEditorSection = TemplateEditorSections.First(s => s.Id == id);
        RelocalizeTemplatePreview();
    }

    private void RebuildTemplateEditorGroups()
    {
        TemplateEditorGroups.Clear();
        var fields = AppearanceFields.ToDictionary(f => f.Key, StringComparer.Ordinal);
        foreach (var group in TemplateEditorLayout.Groups.Where(g => g.SectionId == _selectedTemplateEditorSection?.Id))
        {
            var members = group.Keys.Select(key => fields[key]).ToArray();
            TemplateEditorGroups.Add(new(group.Id, TemplateEditorLayout.L(_settings.Language, group.Ru, group.Uk, group.En), members)
            {
                PaletteActionLabel = TemplateEditorLayout.L(_settings.Language,"Цветовая схема…","Колірна схема…","Color scheme…"),
                Help = group.Id == "legacy-fields" ? TemplateEditorLayout.L(_settings.Language,
                    "Общий запрет действует на все записи. Если поле разрешено здесь, его печать, порядок и формат определяются правилом маппинга выше.",
                    "Загальна заборона діє на всі записи. Якщо поле дозволене тут, його друк, порядок і формат визначає правило мапінгу вище.",
                    "A global restriction applies to all entries. If allowed here, printing, order and formatting are controlled by the mapping rule above.") : ""
            });
        }
        RefreshTemplateEditorVisibility();
    }

    private void RefreshTemplateEditorVisibility()
    {
        foreach (var field in AppearanceFields)
        {
            // A switch to individual icon coloring may freeze a formerly linked tint.
            if (field.Key == "entry_icon_tint_color" && field.TextValue != _appearance.EntryIconTintColor)
                field.Reload();
            field.EditorVisible = TemplateEditorLayout.Visible(field.Key, _appearance);
            field.RefreshColorPresentation();
        }
        foreach (var group in TemplateEditorGroups) group.RefreshVisibility();
    }

    public void NavigateTemplateEditor(string sectionId)
    {
        var section = TemplateEditorSections.FirstOrDefault(s => s.Id == sectionId);
        if (section is null) return;
        ActiveTab = 1;
        SelectedTemplateEditorSection = section;
    }

    private void OnTemplateEditorTabChanged()
    {
        Changed(nameof(TemplateEditorActive));
        _templateHoverRegion=null;
        ClearTemplatePreviewSelection();
        NotifyTemplateHighlight();
        if (!TemplateEditorActive) ResetTemplatePreview();
    }
}
