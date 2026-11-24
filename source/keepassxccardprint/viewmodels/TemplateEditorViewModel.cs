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
            Changed(nameof(TemplateAppearanceMaxHeight));
            RebuildTemplateEditorGroups();
            HighlightTemplateGroup("");
        }
    }
    public bool TemplateMappingVisible => _selectedTemplateEditorSection?.Id == "mapping";
    public int TemplateAppearanceRow => TemplateMappingVisible ? 1 : 0;
    public int TemplateAppearanceRowSpan => TemplateMappingVisible ? 1 : 2;
    public double TemplateAppearanceMaxHeight => TemplateMappingVisible ? 150 : double.PositiveInfinity;
    public bool TemplateEditorActive => ActiveTab == 1;

    private void RelocalizeTemplateEditor()
    {
        var id = _selectedTemplateEditorSection?.Id ?? "general";
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
        NotifyTemplateHighlight();
        if (!TemplateEditorActive) ResetTemplatePreview();
    }
}
