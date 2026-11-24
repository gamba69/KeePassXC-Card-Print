using System.ComponentModel;
using KeePassXCCardPrint.ViewModels;

namespace KeePassXCCardPrint.Services;

public sealed record TemplateEditorSection(string Id, string Label)
{
    public FluentIcons.Common.Icon Glyph => Id switch
    {
        "general" => FluentIcons.Common.Icon.Settings,
        "mapping" => FluentIcons.Common.Icon.Table,
        "header" => FluentIcons.Common.Icon.DocumentHeader,
        "fields" => FluentIcons.Common.Icon.TextBulletList,
        "footer" => FluentIcons.Common.Icon.DocumentFooter,
        "background" => FluentIcons.Common.Icon.Color,
        "recovery" => FluentIcons.Common.Icon.Key,
        "qr" => FluentIcons.Common.Icon.QrCode,
        "reverse" => FluentIcons.Common.Icon.DocumentCopy,
        "printing" => FluentIcons.Common.Icon.Print,
        _ => FluentIcons.Common.Icon.DocumentOnePage
    };
}

public sealed class TemplateEditorGroup(string id, string label, IReadOnlyList<AppearanceField> fields) : INotifyPropertyChanged
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public IReadOnlyList<AppearanceField> Fields { get; } = fields;
    public bool IsVisible => Fields.Any(f => f.EditorVisible);
    public bool IsPrintPermissionGroup => Id == "legacy-fields";
    public bool IsPalette => Id == "palette";
    public bool CanEditPalette => IsPalette && Fields.All(f => f.IsEditable);
    public string PaletteActionLabel { get; init; } = "";
    public string Help { get; init; } = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void RefreshVisibility()
    {
        PropertyChanged?.Invoke(this, new(nameof(IsVisible)));
        PropertyChanged?.Invoke(this, new(nameof(CanEditPalette)));
    }
}

// Stable IDs belong to the editor, not to translated labels or to the JSON schema.
// All appearance keys have exactly one owner; do not clone shared properties for faces.
internal static class TemplateEditorLayout
{
    internal sealed record Section(string Id, string Ru, string Uk, string En);
    internal sealed record Group(string SectionId, string Id, string Ru, string Uk, string En, string[] Keys);
    private static Group G(string section, string id, string ru, string uk, string en, string keys) =>
        new(section, id, ru, uk, en, keys.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    internal static readonly Section[] Sections =
    [
        new("general", "Общие параметры", "Загальні параметри", "General"),
        new("mapping", "Маппинг", "Мапінг", "Field mapping"),
        new("header", "Шапка", "Шапка", "Header"),
        new("fields", "Поля и значения", "Поля та значення", "Fields and values"),
        new("footer", "Подвал", "Колонтитул", "Footer"),
        new("background", "Фон и цвета", "Фон і кольори", "Background and colors"),
        new("recovery", "Коды восстановления", "Коди відновлення", "Recovery codes"),
        new("qr", "QR-коды", "QR-коди", "QR codes"),
        new("reverse", "Вторая сторона", "Друга сторона", "Reverse side"),
        new("printing", "Печать и раскладка", "Друк і розкладка", "Print and layout")
    ];
    internal static readonly Group[] Groups =
    [
        G("general", "geometry", "Размер и геометрия", "Розмір і геометрія", "Size and geometry", "width_mm height_mm padding_mm density style"),
        G("general", "frame", "Рамка", "Рамка", "Frame", "frame frame_width_pt corner_radius_mm frame_color"),
        G("header", "title", "Заголовок записи", "Заголовок запису", "Entry title", "title_font title_size_pt title_weight title_align title_color"),
        G("header", "page-badge", "Номер части карточки", "Номер частини картки", "Card part number", "footer_size_pt page_badge_text_color page_badge_background_color"),
        G("header", "header-group", "Название группы", "Назва групи", "Group name", "header_group_show header_group_position header_group_font header_group_size_pt header_group_weight header_group_color header_group_gap_mm header_group_column_pct header_group_align"),
        G("header", "entry-icon", "Иконка записи", "Іконка запису", "Entry icon", "entry_icon_mode entry_icon_size_mm entry_icon_print_mode entry_icon_color_mode entry_icon_tint_color entry_icon_background_color"),
        G("header", "header-layout", "Оформление шапки", "Оформлення шапки", "Header layout", "header_height_mm header_top_mm header_gap_mm header_fill header_accent header_accent_width_mm header_fill_color header_accent_color header_rule_color"),
        G("fields", "labels", "Названия полей", "Назви полів", "Field labels", "label_font label_size_pt label_weight labels_position label_column_pct labels_upper field_label_color"),
        G("fields", "values", "Значения", "Значення", "Values", "secret_font value_size_pt value_weight value_tracking_pt mark_zero short_fit_min_pct split_boost_pct field_value_color password_color"),
        G("fields", "spacing", "Разделы и интервалы", "Розділи та інтервали", "Sections and spacing", "section_headings section_rules line_spacing field_gap_pt adaptive_field_gaps adaptive_gap_max_mm continuation_repeat_label section_heading_color field_rule_color"),
        G("fields", "long-values", "Группировка длинных значений", "Групування довгих значень", "Long value grouping", "group_long_values group_size group_min_length group_spaces password_group_mode"),
        G("fields", "notes", "Заметки", "Нотатки", "Notes", "notes_mode notes_size_pt notes_color"),
        G("mapping", "legacy-fields", "Общие ограничения печати", "Загальні обмеження друку", "Global print restrictions", "print_username print_password print_url print_notes print_custom print_group"),
        G("footer", "footer-layout", "Расположение", "Розташування", "Layout", "footer_height_mm footer_bottom_shift_mm footer_rule_color"),
        G("footer", "uuid", "Идентификатор UUID", "Ідентифікатор UUID", "UUID", "footer_uuid_show footer_uuid_length footer_uuid_separators footer_uuid_uppercase footer_uuid_prefix footer_uuid_size_pt footer_uuid_weight footer_uuid_color"),
        G("footer", "date", "Дата", "Дата", "Date", "show_date footer_date_format footer_date_size_pt footer_date_weight footer_date_color"),
        G("background", "palette", "Общая палитра обеих сторон", "Спільна палітра обох сторін", "Shared palette", "background_color ink_color muted_color accent_color header_color rule_color border_color palette_icon_color_mode"),
        G("background", "common-background", "Общий фоновый узор", "Спільний фоновий візерунок", "Shared background pattern", "card_bg_pattern card_bg_gray card_bg_contrast"),
        G("background", "front-background", "Фон лицевой стороны (иначе общий)", "Фон лицевого боку (інакше спільний)", "Front background (otherwise shared)", "front_bg_override front_bg_pattern front_bg_gray front_bg_contrast front_background_color"),
        G("background", "back-background", "Фон оборота с данными (иначе общий)", "Фон звороту з даними (інакше спільний)", "Data back background (otherwise shared)", "back_bg_override duplex_back_background back_bg_gray back_bg_contrast back_background_color"),
        G("background", "empty-background", "Фон оборота без данных", "Фон звороту без даних", "Empty back background", "empty_back_bg_source empty_back_bg_pattern empty_back_bg_gray empty_back_bg_contrast empty_back_background_color"),
        G("recovery", "recovery-layout", "Компоновка кодов", "Компонування кодів", "Code layout", "recovery_columns recovery_checkboxes recovery_checkbox_color"),
        G("recovery", "recovery-size", "Типографика кодов", "Типографіка кодів", "Code typography", "recovery_autosize recovery_min_font_pt recovery_max_font_pt recovery_value_color"),
        G("qr", "totp", "TOTP и QR-код", "TOTP і QR-код", "TOTP and QR code", "otp_compact totp_qr_enabled totp_qr_position totp_qr_height_mm totp_qr_min_height_mm otp_label_color otp_value_color otp_meta_color"),
        G("reverse", "reverse-use", "Использование второй стороны", "Використання другої сторони", "Use of reverse side", "duplex_enabled duplex_continuation duplex_combine_different"),
        G("reverse", "empty-reverse", "Оборот без данных", "Зворот без даних", "Empty reverse", "duplex_back_fill empty_back_show_header empty_back_show_footer empty_back_show_frame"),
        G("reverse", "caption", "Декоративная надпись", "Декоративний напис", "Decorative caption", "empty_back_show_art duplex_back_language"),
        G("reverse", "caption-title", "Верхняя надпись", "Верхній напис", "Upper caption", "back_caption_title_font back_caption_title_size_pt back_caption_title_weight back_caption_title_color"),
        G("reverse", "caption-subtitle", "Нижняя надпись", "Нижній напис", "Lower caption", "back_caption_subtitle_font back_caption_subtitle_size_pt back_caption_subtitle_weight back_caption_subtitle_color"),
        G("reverse", "back-icon", "Фоновая иконка", "Фонова іконка", "Reverse emblem", "empty_back_show_icon back_art_icon back_art_icon_size_mm back_art_icon_color back_art_icon_opacity"),
        G("reverse", "back-composition", "Композиция оборота", "Композиція звороту", "Reverse composition", "back_art_icon_position back_art_text_gap_mm"),
        G("reverse", "back-rules", "Линии оборота", "Лінії звороту", "Reverse rules", "duplex_back_style back_rules_style back_rules_width_mm back_rules_thickness_mm back_rules_gap_mm back_rules_opacity back_rules_color"),
        G("printing", "cut", "Метки резки", "Позначки різання", "Cut guides", "crop_marks cut_guide_mode cut_cross_size_mm cut_cross_width_pt"),
        G("printing", "alignment", "Переворот и совмещение сторон", "Переворот і суміщення сторін", "Duplex alignment", "duplex_flip duplex_back_offset_x_mm duplex_back_offset_y_mm")
    ];

    internal static string L(string language, string ru, string uk, string en) =>
        Localizer.ResolveLanguage(language) switch { "uk" => uk, "en" => en, _ => ru };

    internal static string GroupCaption(string groupId,string language)
    {
        var group=Groups.FirstOrDefault(g=>g.Id==groupId);
        return group is null ? "" : L(language,group.Ru,group.Uk,group.En);
    }

    internal static void Validate(IEnumerable<string> descriptorKeys)
    {
        var actual = Groups.SelectMany(g => g.Keys).ToArray();
        var expected = descriptorKeys.ToArray();
        if (actual.Length != actual.Distinct(StringComparer.Ordinal).Count() ||
            expected.Length != expected.Distinct(StringComparer.Ordinal).Count() ||
            !actual.ToHashSet(StringComparer.Ordinal).SetEquals(expected))
            throw new InvalidOperationException("Template editor must own every appearance key exactly once.");
    }

    // Hide only controls that cannot affect output under their current parent mode.
    // Never change stored settings when toggling a parent.
    internal static bool Visible(string key, CardAppearance a)
    {
        bool decorated = a.DuplexEnabled && a.DuplexBackFill != "blank";
        bool caption = decorated && a.EmptyBackShowArt;
        bool icon = decorated && a.EmptyBackShowIcon;
        if (key.StartsWith("back_caption_", StringComparison.Ordinal)) return caption;
        if (key == "back_art_icon_position") return caption || (icon && a.BackArtIcon != "none");
        if (key == "back_art_text_gap_mm") return caption && icon && a.BackArtIcon != "none";
        if (key.StartsWith("back_art_", StringComparison.Ordinal))
            return icon && (key == "back_art_icon" || a.BackArtIcon != "none");
        if (key.StartsWith("back_rules_", StringComparison.Ordinal))
            return caption && a.DuplexBackStyle == "rules";
        if (key.StartsWith("header_group_", StringComparison.Ordinal) && key != "header_group_show")
            return a.HeaderGroupShow && (key != "header_group_column_pct" || a.HeaderGroupPosition == "left");
        if (key.StartsWith("footer_uuid_", StringComparison.Ordinal) && key != "footer_uuid_show") return a.FooterUuidShow;
        if (key.StartsWith("footer_date_", StringComparison.Ordinal)) return a.ShowDate;
        return key switch
        {
            "frame_width_pt" or "frame_color" => a.Frame,
            // The radius is also used by header fill and accent decoration.
            "corner_radius_mm" => a.Frame || (a.HeaderFill && a.Style != "minimal") || a.HeaderAccent,
            "header_fill_color" => a.HeaderFill && a.Style != "minimal",
            "header_accent_color" => a.HeaderAccent,
            "header_rule_color" => !a.HeaderAccent && a.SectionRules,
            "page_badge_background_color" => a.Style == "archive" && a.HeaderFill,
            "entry_icon_color_mode" => a.EntryIconMode != "none" && a.EntryIconPrintMode != "none",
            "entry_icon_tint_color" => CardIconColoring.ShowTint(a),
            "entry_icon_background_color" => a.EntryIconMode != "none" && a.EntryIconPrintMode is "auto" or "backdrop",
            "section_heading_color" => a.SectionHeadings,
            "field_rule_color" => a.SectionRules || a.Style == "table",
            "footer_rule_color" => a.SectionRules,
            "recovery_checkbox_color" => a.RecoveryCheckboxes,
            "front_background_color" => a.FrontBgOverride,
            "back_background_color" => a.DuplexEnabled && a.BackBgOverride,
            "empty_back_background_color" => decorated && a.EmptyBackBgSource == "custom",
            "header_accent_width_mm" => a.HeaderAccent,
            "entry_icon_size_mm" => a.EntryIconMode != "none" && a.EntryIconPrintMode != "none",
            "notes_size_pt" => a.NotesMode is "small" or "optional",
            "recovery_min_font_pt" or "recovery_max_font_pt" => a.RecoveryAutosize,
            "card_bg_contrast" => a.CardBgPattern != "clean",
            "front_bg_pattern" or "front_bg_gray" => a.FrontBgOverride,
            "front_bg_contrast" => a.FrontBgOverride && a.FrontBgPattern != "clean",
            "back_bg_override" => a.DuplexEnabled,
            "duplex_back_background" or "back_bg_gray" => a.DuplexEnabled && a.BackBgOverride,
            "back_bg_contrast" => a.DuplexEnabled && a.BackBgOverride && a.DuplexBackBackground != "clean",
            "empty_back_bg_source" => decorated,
            "empty_back_bg_pattern" or "empty_back_bg_gray" => decorated && a.EmptyBackBgSource == "custom",
            "empty_back_bg_contrast" => decorated && a.EmptyBackBgSource == "custom" && a.EmptyBackBgPattern != "clean",
            "duplex_continuation" or "duplex_combine_different" or "duplex_back_fill" or "duplex_flip" or "duplex_back_offset_x_mm" or "duplex_back_offset_y_mm" => a.DuplexEnabled,
            "empty_back_show_header" or "empty_back_show_footer" or "empty_back_show_art" or "empty_back_show_icon" => decorated,
            "empty_back_show_frame" => decorated && a.Frame,
            "duplex_back_language" or "duplex_back_style" => caption,
            "totp_qr_position" or "totp_qr_height_mm" or "totp_qr_min_height_mm" => a.TotpQrEnabled,
            "entry_icon_print_mode" => a.EntryIconMode != "none",
            "adaptive_gap_max_mm" => a.AdaptiveFieldGaps,
            "label_column_pct" => a.LabelsPosition == "left",
            _ => true
        };
    }
}
