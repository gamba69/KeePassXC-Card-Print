using KeePassXCCardPrint.Platforms;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.IO;
using Avalonia.Media;
using KeePassXCCardPrint.Rendering;

namespace KeePassXCCardPrint.Services;

public sealed class DesignChoice(string code)
{
    public string Code { get; } = code;
    private static readonly Lazy<IReadOnlyDictionary<string, Geometry>> IconPreviews = new(() =>
    {
        var result = new Dictionary<string, Geometry>(StringComparer.Ordinal);
        foreach (var id in new[] { "shield", "lock", "key", "card", "vault" })
            result.Add(id, Geometry.Parse(FluentCardGlyphs.PathData(id)!));
        return result;
    });
    public bool HasIconPreview => FluentCardGlyphs.PathData(Code) is not null;
    public Geometry? IconGeometry => HasIconPreview ? IconPreviews.Value[Code] : null;
    public string Label => Code switch
    {
        "true" => "Да", "false" => "Нет", "archive" => "Архив", "technical" => "Технический", "classic" => "Классический", "table" => "Табличный",
        "minimal" => "Минимальный", "normal" => "Обычная", "compact" => "Компактная", "large" => "Крупная", "spacious" => "Крупная",
        "left" => "Слева", "center" => "По центру", "right" => "Справа", "top" => "Сверху", "every4" => "Каждые 4", "standard" => "Стандартный UUID", "yyyy-MM-dd" => "2026-11-03", "dd.MM.yyyy" => "03.11.2026", "yyyy/MM/dd" => "2026/11/03",
        "above" => "Сверху", "all" => "Все", "custom" => "Только свои", "none" => "Нет",
        "original" => "Оригинальная", "auto" => "Авто", "outline" => "Контур", "silhouette" => "Силуэт",
        "backdrop" => "С подложкой", "always" => "Всегда", "never" => "Никогда", "on" => "Да", "off" => "Нет",
        "inherit" => "Как в оформлении", "clean" => "Чистый", "watermark" => "Водяной знак",
        "gradient" => "Мягкий градиент", "diagonal" => "Диагональные полосы", "texture" => "Текстура",
        "rules" => "С линиями", "front" => "Как на лицевой", "back" => "Как у оборота", "shield" => "Щит", "lock" => "Замок", "key" => "Ключ", "card" => "Карточка", "vault" => "Сейф", "stripe" => "Полосы", "simple" => "Упрощённые", "design" => "Оформление", "blank" => "Пустой", "long" => "По длинной", "short" => "По короткой",
        "ru" => "Русский", "uk" => "Українська", "en" => "English", "regular" => "Обычный",
        "medium" => "Medium", "semibold" => "SemiBold", "bold" => "Bold",
        "200" => "ExtraLight (200)", "300" => "Light (300)", "400" => "Regular (400)",
        "500" => "Medium (500)", "600" => "SemiBold (600)", "700" => "Bold (700)",
        "800" => "ExtraBold (800)",
        _ => Code
    };
    public override string ToString() => Label;
}

// Editable, reflection-free AOT-safe descriptor.
public sealed class AppearanceField : INotifyPropertyChanged
{
    public string Group { get; }
    public string Label { get; }
    public string Key { get; }
    public string Description { get; }
    public double EditorWidth => IsNumeric ? 96 : IsColor ? 140 : 187;
    public bool IsColor => Key.EndsWith("_color",StringComparison.Ordinal);
    public IBrush? SwatchBrush => Color.TryParse(_text,out var color) ? new SolidColorBrush(color) : null;
    private readonly CardAppearance _appearance;
    private readonly Func<CardAppearance,string> _read;
    private readonly Action<CardAppearance,string> _write;
    private readonly string _type;
    private string _text = "";
    // A built-in preset is read-only even if a control sends a stale binding update.
    private bool _isReadOnly;
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            if (_isReadOnly == value) return;
            _isReadOnly = value;
            Raise();
            Raise(nameof(IsEditable));
        }
    }
    public bool IsEditable => !IsReadOnly;
    private bool _checked;
    private DesignChoice? _selected;
    public bool IsBool { get; }
    public bool IsChoice { get; }
    public bool IsNumeric { get; }
    public bool IsInteger { get; }
    public bool IsText => !IsBool && !IsChoice;
    public ObservableCollection<DesignChoice> Choices { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ValueChanged;
    public string TextValue
    {
        get => _text;
        set { if (IsReadOnly || _text == value) return; _text = value ?? ""; Raise();
              if (IsText) Commit(_text); }
    }
    public bool Checked
    {
        get => _checked;
        set { if (IsReadOnly || _checked == value) return; _checked = value; Raise(); Commit(value ? "true" : "false"); }
    }
    public DesignChoice? SelectedChoice
    {
        get => _selected;
        set { if (IsReadOnly || ReferenceEquals(_selected, value)) return; _selected=value; Raise();
              if (value is not null && IsChoice) Commit(value.Code); }
    }
    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this,new(name));
    private void Commit(string text) { _write(_appearance,text); Reload(); ValueChanged?.Invoke(this,EventArgs.Empty); }

    // Steps are in the field's physical units (mm, pt, percent or characters).
    // Tiny header/footer adjustments must not visibly jump the layout.
    private double NudgeStep() => Key switch
    {
        "header_group_gap_mm" or "header_accent_width_mm" or "back_rules_thickness_mm" => .05,
        "card_bg_gray" or "card_bg_contrast" or "front_bg_gray" or "front_bg_contrast" or
        "back_bg_gray" or "back_bg_contrast" or "empty_back_bg_gray" or "empty_back_bg_contrast" or
        "back_art_icon_opacity" or "back_rules_opacity" => 5,
        "header_group_size_pt" or "footer_uuid_size_pt" or "footer_date_size_pt" => .1,
        "header_height_mm" or "header_top_mm" or "header_gap_mm" or "footer_height_mm" or "footer_bottom_shift_mm" => .1,
        "header_group_column_pct" or "footer_uuid_length" => 1,
        "frame_width_pt" or "cut_cross_width_pt" => .05,
        "value_tracking_pt" or "group_spaces" => .05,
        "line_spacing" => .01,
        _ when IsInteger => 1,
        _ when Key.EndsWith("_pt", StringComparison.Ordinal) => .1,
        _ when Key.EndsWith("_mm", StringComparison.Ordinal) => .1,
        _ => .1
    };

    private (double Min, double Max) NudgeBounds() => Key switch
    {
        "card_bg_gray" or "card_bg_contrast" or "front_bg_gray" or "front_bg_contrast" or
        "back_bg_gray" or "back_bg_contrast" or "empty_back_bg_gray" or "empty_back_bg_contrast" or
        "back_art_icon_opacity" or "back_rules_opacity" => (0, 100),
        "back_art_icon_size_mm" => (4, 28),
        "back_art_text_gap_mm" => (2, 12),
        "back_rules_width_mm" => (8, 55),
        "back_rules_thickness_mm" => (.05, 1),
        "back_rules_gap_mm" => (.1, 8),
        "header_height_mm" => (0, 50),
        "header_top_mm" => (.9, 20),
        "header_gap_mm" => (0, 40),
        "footer_height_mm" => (2, 20),
        "footer_bottom_shift_mm" => (0, _appearance.FooterMaximumBottomShiftMm),
        "header_group_size_pt" => (2, 24),
        "header_group_gap_mm" => (0, 10),
        "header_group_column_pct" => (15, 55),
        "header_accent_width_mm" => (.2, 3),
        "footer_uuid_length" => (4, 32),
        "footer_uuid_size_pt" or "footer_date_size_pt" => (2, 18),
        "frame_width_pt" or "cut_cross_width_pt" => (0, 5),
        "group_spaces" => (0, 2),
        "line_spacing" => (.8, 2),
        _ => (double.NegativeInfinity, double.PositiveInfinity)
    };

    public void Nudge(int direction)
    {
        if (IsReadOnly || !IsNumeric || direction == 0) return;
        var normalized = (_text ?? string.Empty).Replace(',', '.');
        double current;
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out current)
            || !double.IsFinite(current))
            return; // A partially typed value must not be reset to zero.
        var (min,max) = NudgeBounds();
        double next = Math.Clamp(Math.Round(current + Math.Sign(direction)*NudgeStep(),
            3, MidpointRounding.AwayFromZero),min,max);
        string newText = IsInteger ? ((int)Math.Round(next)).ToString(CultureInfo.InvariantCulture)
                                   : next.ToString("0.###", CultureInfo.InvariantCulture);
        TextValue = newText;
    }
    public AppearanceField(string group,string label,string key,string type,CardAppearance appearance,
                           Func<CardAppearance,string> read,Action<CardAppearance,string> write,string? options)
    {
        Group=group;Label=label;Key=key;Description=AppearanceDescriptions.For(key,label);_appearance=appearance;_read=read;_write=write;_type=type;
        IsBool=type=="bool";IsChoice=options is not null; IsInteger=type=="int"; IsNumeric=type is "int" or "double";
        if (options is not null) foreach(var code in options.Split(','))Choices.Add(new DesignChoice(code));
        Reload();
    }
    public void Reload()
    {
        _text = _read(_appearance);
        if(Key.EndsWith("_weight",StringComparison.Ordinal))
        {
            _text=FontWeights.Normalize(_text);
            _write(_appearance,_text); // Keep the displayed weight in canonical numeric form
        }
        Raise(nameof(TextValue));Raise(nameof(SwatchBrush));
        _checked=string.Equals(_text,"true",StringComparison.OrdinalIgnoreCase);Raise(nameof(Checked));
        _selected=Choices.FirstOrDefault(x=>x.Code==_text) ?? Choices.FirstOrDefault();Raise(nameof(SelectedChoice));
    }
}

public static class AppearanceDescriptor
{
    private static int ParseInt(string s,int fallback) => int.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out var v) ? v : fallback;
    private static double ParseDouble(string s,double fallback) => double.TryParse(s.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var v) && double.IsFinite(v) ? v : fallback;
    public static IReadOnlyList<AppearanceField> Create(CardAppearance model)
    {
        var result = new List<AppearanceField>(140);
        void Add(string group,string label,string key,string type,Func<CardAppearance,string> reader,Action<CardAppearance,string> writer,string? choices)
            => result.Add(new AppearanceField(group,label,key,type,model,reader,writer,choices));
            Add("Геометрия", "Ширина, мм", "width_mm", "double", a => a.WidthMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.WidthMm = ParseDouble(v,a.WidthMm), null);
            Add("Геометрия", "Высота, мм", "height_mm", "double", a => a.HeightMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.HeightMm = ParseDouble(v,a.HeightMm), null);
            Add("Геометрия", "Плотность", "density", "string", a => a.Density.ToString(), (a,v) => a.Density = v, "compact,normal,large");
            Add("Геометрия", "Стиль", "style", "string", a => a.Style.ToString(), (a,v) => a.Style = v, "archive,technical,classic,minimal,table");
            Add("Шрифты", "Моноширинный шрифт", "secret_font", "string", a => a.SecretFont.ToString(), (a,v) => a.SecretFont = v, "Atkinson Hyperlegible Mono,JetBrains Mono NL,Recursive Mono,IBM Plex Mono,Source Code Pro,Geist Mono,Cascadia Mono");
            Add("Шрифты", "Шрифт заголовка", "title_font", "string", a => a.TitleFont.ToString(), (a,v) => a.TitleFont = v, PlatformServices.Current.SystemFontOptions);
            Add("Шрифты", "Шрифт подписей", "label_font", "string", a => a.LabelFont.ToString(), (a,v) => a.LabelFont = v, PlatformServices.Current.SystemFontOptions);
            Add("Шрифты", "Заголовок, pt", "title_size_pt", "double", a => a.TitleSizePt.ToString(CultureInfo.InvariantCulture), (a,v) => a.TitleSizePt = ParseDouble(v,a.TitleSizePt), null);
            Add("Шрифты", "Подписи, pt", "label_size_pt", "double", a => a.LabelSizePt.ToString(CultureInfo.InvariantCulture), (a,v) => a.LabelSizePt = ParseDouble(v,a.LabelSizePt), null);
            Add("Шрифты", "Значения, pt", "value_size_pt", "double", a => a.ValueSizePt.ToString(CultureInfo.InvariantCulture), (a,v) => a.ValueSizePt = ParseDouble(v,a.ValueSizePt), null);
            Add("Шрифты", "Вес заголовка", "title_weight", "string", a => a.TitleWeight, (a,v) => a.TitleWeight = v, "200,300,400,500,600,700,800");
            Add("Шрифты", "Вес подписей", "label_weight", "string", a => a.LabelWeight, (a,v) => a.LabelWeight = v, "200,300,400,500,600,700,800");
            Add("Шрифты", "Вес значений", "value_weight", "string", a => a.ValueWeight, (a,v) => a.ValueWeight = v, "200,300,400,500,600,700,800");
            Add("Шрифты", "Интервал букв, pt", "value_tracking_pt", "double", a => a.ValueTrackingPt.ToString(CultureInfo.InvariantCulture), (a,v) => a.ValueTrackingPt = ParseDouble(v,a.ValueTrackingPt), null);
            Add("Шрифты", "Межстрочный интервал", "line_spacing", "double", a => a.LineSpacing.ToString(CultureInfo.InvariantCulture), (a,v) => a.LineSpacing = ParseDouble(v,a.LineSpacing), null);
            Add("Шрифты", "Пробел между полями, pt", "field_gap_pt", "double", a => a.FieldGapPt.ToString(CultureInfo.InvariantCulture), (a,v) => a.FieldGapPt = ParseDouble(v,a.FieldGapPt), null);
            Add("Шрифты", "Умные интервалы", "adaptive_field_gaps", "bool", a => a.AdaptiveFieldGaps.ToString(), (a,v) => a.AdaptiveFieldGaps = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Шрифты", "Макс. добавочный интервал, мм", "adaptive_gap_max_mm", "double", a => a.AdaptiveGapMaxMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.AdaptiveGapMaxMm = ParseDouble(v,a.AdaptiveGapMaxMm), null);
            Add("Геометрия", "Внутренний отступ, мм", "padding_mm", "double", a => a.PaddingMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.PaddingMm = ParseDouble(v,a.PaddingMm), null);
            Add("Геометрия", "Толщина рамки, pt", "frame_width_pt", "double", a => a.FrameWidthPt.ToString(CultureInfo.InvariantCulture), (a,v) => a.FrameWidthPt = ParseDouble(v,a.FrameWidthPt), null);
            Add("Геометрия", "Закругление, мм", "corner_radius_mm", "double", a => a.CornerRadiusMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.CornerRadiusMm = ParseDouble(v,a.CornerRadiusMm), null);
            Add("Геометрия", "Заголовок: выравнивание", "title_align", "string", a => a.TitleAlign.ToString(), (a,v) => a.TitleAlign = v, "left,center,right");
            Add("Шапка и подвал", "Высота шапки, мм", "header_height_mm", "double", a => a.HeaderHeightMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.HeaderHeightMm = Math.Clamp(ParseDouble(v,a.HeaderHeightMm),0,50), null);
            Add("Шапка и подвал", "Верхний отступ шапки, мм", "header_top_mm", "double", a => a.HeaderTopMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.HeaderTopMm = Math.Clamp(ParseDouble(v,a.HeaderTopMm),.9,20), null);
            Add("Шапка и подвал", "Интервал под шапкой, мм", "header_gap_mm", "double", a => a.HeaderGapMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.HeaderGapMm = Math.Clamp(ParseDouble(v,a.HeaderGapMm),0,40), null);
            Add("Шапка и подвал", "Высота подвала, мм", "footer_height_mm", "double", a => a.FooterHeightMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.FooterHeightMm = Math.Clamp(ParseDouble(v,a.FooterHeightMm),2,20), null);
            Add("Шапка и подвал", "Сдвиг подвала вниз, мм", "footer_bottom_shift_mm", "double", a => a.FooterBottomShiftMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.FooterBottomShiftMm = Math.Clamp(ParseDouble(v,a.FooterBottomShiftMm),0,a.FooterMaximumBottomShiftMm), null);
            Add("Шапка и подвал", "Размер плашки страниц, pt", "footer_size_pt", "double", a => a.FooterSizePt.ToString(CultureInfo.InvariantCulture), (a,v) => a.FooterSizePt = Math.Clamp(ParseDouble(v,a.FooterSizePt),2,15), null);
            Add("Шапка и подвал", "Группа в шапке", "header_group_show", "bool", a => a.HeaderGroupShow.ToString(), (a,v) => a.HeaderGroupShow = v.Equals("true", StringComparison.OrdinalIgnoreCase), null);
            Add("Шапка и подвал", "Положение группы", "header_group_position", "string", a => a.HeaderGroupPosition, (a,v) => a.HeaderGroupPosition=v, "left,top");
            Add("Шапка и подвал", "Шрифт группы", "header_group_font", "string", a => a.HeaderGroupFont, (a,v) => a.HeaderGroupFont=v, PlatformServices.Current.SystemFontOptions);
            Add("Шапка и подвал", "Размер группы, pt", "header_group_size_pt", "double", a => a.HeaderGroupSizePt.ToString(CultureInfo.InvariantCulture), (a,v) => a.HeaderGroupSizePt=Math.Clamp(ParseDouble(v,a.HeaderGroupSizePt),2,24), null);
            Add("Шапка и подвал", "Вес группы", "header_group_weight", "string", a => a.HeaderGroupWeight, (a,v) => a.HeaderGroupWeight=v, "200,300,400,500,600,700,800");
            Add("Шапка и подвал", "Цвет группы", "header_group_color", "string", a => a.HeaderGroupColor, (a,v) => a.HeaderGroupColor=v, null);
            Add("Шапка и подвал", "Зазор группа/название, мм", "header_group_gap_mm", "double", a => a.HeaderGroupGapMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.HeaderGroupGapMm=Math.Clamp(ParseDouble(v,a.HeaderGroupGapMm),0,10), null);
            Add("Шапка и подвал", "Ширина группы слева, %", "header_group_column_pct", "int", a => a.HeaderGroupColumnPct.ToString(CultureInfo.InvariantCulture), (a,v) => a.HeaderGroupColumnPct=Math.Clamp(ParseInt(v,a.HeaderGroupColumnPct),15,55), null);
            Add("Шапка и подвал", "Выравнивание группы", "header_group_align", "string", a => a.HeaderGroupAlign, (a,v) => a.HeaderGroupAlign=v, "left,center,right");
            Add("Шапка и подвал", "Толщина акцента, мм", "header_accent_width_mm", "double", a => a.HeaderAccentWidthMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.HeaderAccentWidthMm=Math.Clamp(ParseDouble(v,a.HeaderAccentWidthMm),.2,3), null);
            Add("Шапка и подвал", "UUID в подвале", "footer_uuid_show", "bool", a => a.FooterUuidShow.ToString(), (a,v) => a.FooterUuidShow=v.Equals("true",StringComparison.OrdinalIgnoreCase), null);
            Add("Шапка и подвал", "Длина UUID", "footer_uuid_length", "int", a => a.FooterUuidLength.ToString(CultureInfo.InvariantCulture), (a,v) => a.FooterUuidLength=Math.Clamp(ParseInt(v,a.FooterUuidLength),4,32), null);
            Add("Шапка и подвал", "Разделители UUID", "footer_uuid_separators", "string", a => a.FooterUuidSeparators, (a,v) => a.FooterUuidSeparators=v, "none,every4,standard");
            Add("Шапка и подвал", "UUID прописными", "footer_uuid_uppercase", "bool", a => a.FooterUuidUppercase.ToString(), (a,v) => a.FooterUuidUppercase=v.Equals("true",StringComparison.OrdinalIgnoreCase), null);
            Add("Шапка и подвал", "Подпись ID:", "footer_uuid_prefix", "bool", a => a.FooterUuidPrefix.ToString(), (a,v) => a.FooterUuidPrefix=v.Equals("true",StringComparison.OrdinalIgnoreCase), null);
            Add("Шапка и подвал", "Размер UUID, pt", "footer_uuid_size_pt", "double", a => a.FooterUuidSizePt.ToString(CultureInfo.InvariantCulture), (a,v) => a.FooterUuidSizePt=Math.Clamp(ParseDouble(v,a.FooterUuidSizePt),2,18), null);
            Add("Шапка и подвал", "Вес UUID", "footer_uuid_weight", "string", a => a.FooterUuidWeight, (a,v) => a.FooterUuidWeight=v, "200,300,400,500,600,700,800");
            Add("Шапка и подвал", "Цвет UUID", "footer_uuid_color", "string", a => a.FooterUuidColor, (a,v) => a.FooterUuidColor=v, null);
            Add("Шапка и подвал", "Формат даты", "footer_date_format", "string", a => a.FooterDateFormat, (a,v) => a.FooterDateFormat=v, "yyyy-MM-dd,dd.MM.yyyy,yyyy/MM/dd");
            Add("Шапка и подвал", "Размер даты, pt", "footer_date_size_pt", "double", a => a.FooterDateSizePt.ToString(CultureInfo.InvariantCulture), (a,v) => a.FooterDateSizePt=Math.Clamp(ParseDouble(v,a.FooterDateSizePt),2,18), null);
            Add("Шапка и подвал", "Вес даты", "footer_date_weight", "string", a => a.FooterDateWeight, (a,v) => a.FooterDateWeight=v, "200,300,400,500,600,700,800");
            Add("Шапка и подвал", "Цвет даты", "footer_date_color", "string", a => a.FooterDateColor, (a,v) => a.FooterDateColor=v, null);
            Add("Иконки и цвета", "Какие иконки выводить", "entry_icon_mode", "string", a => a.EntryIconMode.ToString(), (a,v) => a.EntryIconMode = v, "none,custom,all");
            Add("Иконки и цвета", "Размер иконки, мм", "entry_icon_size_mm", "double", a => a.EntryIconSizeMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.EntryIconSizeMm = ParseDouble(v,a.EntryIconSizeMm), null);
            Add("Поля и содержимое", "Положение названий", "labels_position", "string", a => a.LabelsPosition.ToString(), (a,v) => a.LabelsPosition = v, "above,left");
            Add("Поля и содержимое", "Колонка названий, %", "label_column_pct", "int", a => a.LabelColumnPct.ToString(CultureInfo.InvariantCulture), (a,v) => a.LabelColumnPct = ParseInt(v,a.LabelColumnPct), null);
            Add("Поля и содержимое", "Названия прописными", "labels_upper", "bool", a => a.LabelsUpper.ToString(), (a,v) => a.LabelsUpper = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Поля и содержимое", "Заголовки разделов", "section_headings", "bool", a => a.SectionHeadings.ToString(), (a,v) => a.SectionHeadings = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Поля и содержимое", "Линии разделов", "section_rules", "bool", a => a.SectionRules.ToString(), (a,v) => a.SectionRules = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Поля и содержимое", "Заливка шапки", "header_fill", "bool", a => a.HeaderFill.ToString(), (a,v) => a.HeaderFill = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Поля и содержимое", "Акцент в шапке", "header_accent", "bool", a => a.HeaderAccent.ToString(), (a,v) => a.HeaderAccent = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Коды восстановления", "Колонок кодов", "recovery_columns", "int", a => a.RecoveryColumns.ToString(CultureInfo.InvariantCulture), (a,v) => a.RecoveryColumns = ParseInt(v,a.RecoveryColumns), null);
            Add("Коды восстановления", "Чекбоксы кодов", "recovery_checkboxes", "bool", a => a.RecoveryCheckboxes.ToString(), (a,v) => a.RecoveryCheckboxes = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Коды восстановления", "Авторазмер кодов", "recovery_autosize", "bool", a => a.RecoveryAutosize.ToString(), (a,v) => a.RecoveryAutosize = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Коды восстановления", "Минимум кодов, pt", "recovery_min_font_pt", "double", a => a.RecoveryMinFontPt.ToString(CultureInfo.InvariantCulture), (a,v) => a.RecoveryMinFontPt = ParseDouble(v,a.RecoveryMinFontPt), null);
            Add("Коды восстановления", "Максимум кодов, pt", "recovery_max_font_pt", "double", a => a.RecoveryMaxFontPt.ToString(CultureInfo.InvariantCulture), (a,v) => a.RecoveryMaxFontPt = ParseDouble(v,a.RecoveryMaxFontPt), null);
            Add("Поля и содержимое", "Повторять названия при переносе", "continuation_repeat_label", "bool", a => a.ContinuationRepeatLabel.ToString(), (a,v) => a.ContinuationRepeatLabel = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Поля и содержимое", "Компактный TOTP", "otp_compact", "bool", a => a.OtpCompact.ToString(), (a,v) => a.OtpCompact = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Иконки и цвета", "Фон", "background_color", "string", a => a.BackgroundColor.ToString(), (a,v) => a.BackgroundColor = v, null);
            Add("Иконки и цвета", "Основной текст", "ink_color", "string", a => a.InkColor.ToString(), (a,v) => a.InkColor = v, null);
            Add("Иконки и цвета", "Второстепенный текст", "muted_color", "string", a => a.MutedColor.ToString(), (a,v) => a.MutedColor = v, null);
            Add("Иконки и цвета", "Акцент", "accent_color", "string", a => a.AccentColor.ToString(), (a,v) => a.AccentColor = v, null);
            Add("Иконки и цвета", "Фон заголовка", "header_color", "string", a => a.HeaderColor.ToString(), (a,v) => a.HeaderColor = v, null);
            Add("Иконки и цвета", "Разделители", "rule_color", "string", a => a.RuleColor.ToString(), (a,v) => a.RuleColor = v, null);
            Add("Иконки и цвета", "Рамка", "border_color", "string", a => a.BorderColor.ToString(), (a,v) => a.BorderColor = v, null);
            Add("Шрифты", "Выделять нули", "mark_zero", "bool", a => a.MarkZero.ToString(), (a,v) => a.MarkZero = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Поля и содержимое", "Логин", "print_username", "bool", a => a.PrintUsername.ToString(), (a,v) => a.PrintUsername = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Поля и содержимое", "Пароль", "print_password", "bool", a => a.PrintPassword.ToString(), (a,v) => a.PrintPassword = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Поля и содержимое", "URL", "print_url", "bool", a => a.PrintUrl.ToString(), (a,v) => a.PrintUrl = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Поля и содержимое", "Заметки", "print_notes", "bool", a => a.PrintNotes.ToString(), (a,v) => a.PrintNotes = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Поля и содержимое", "Дополнительные поля", "print_custom", "bool", a => a.PrintCustom.ToString(), (a,v) => a.PrintCustom = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Поля и содержимое", "Дата в подвале", "show_date", "bool", a => a.ShowDate.ToString(), (a,v) => a.ShowDate = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Геометрия", "Метки резки (старый режим)", "crop_marks", "bool", a => a.CropMarks.ToString(), (a,v) => a.CropMarks = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Геометрия", "Метки резки", "cut_guide_mode", "string", a => a.CutGuideMode.ToString(), (a,v) => a.CutGuideMode = v, "none,edge,cross,both");
            Add("Геометрия", "Размер крестов, мм", "cut_cross_size_mm", "double", a => a.CutCrossSizeMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.CutCrossSizeMm = ParseDouble(v,a.CutCrossSizeMm), null);
            Add("Геометрия", "Толщина крестов, pt", "cut_cross_width_pt", "double", a => a.CutCrossWidthPt.ToString(CultureInfo.InvariantCulture), (a,v) => a.CutCrossWidthPt = ParseDouble(v,a.CutCrossWidthPt), null);
            Add("Геометрия", "Печатать рамку", "frame", "bool", a => a.Frame.ToString(), (a,v) => a.Frame = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Поля и содержимое", "Название группы", "print_group", "bool", a => a.PrintGroup.ToString(), (a,v) => a.PrintGroup = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Шрифты", "Минимум при уменьшении, %", "short_fit_min_pct", "int", a => a.ShortFitMinPct.ToString(CultureInfo.InvariantCulture), (a,v) => a.ShortFitMinPct = ParseInt(v,a.ShortFitMinPct), null);
            Add("Шрифты", "Увеличение после разделения, %", "split_boost_pct", "int", a => a.SplitBoostPct.ToString(CultureInfo.InvariantCulture), (a,v) => a.SplitBoostPct = ParseInt(v,a.SplitBoostPct), null);
            Add("Поля и содержимое", "Заметки, pt", "notes_size_pt", "double", a => a.NotesSizePt.ToString(CultureInfo.InvariantCulture), (a,v) => a.NotesSizePt = ParseDouble(v,a.NotesSizePt), null);
            Add("Поля и содержимое", "Режим заметок", "notes_mode", "string", a => a.NotesMode.ToString(), (a,v) => a.NotesMode = v, "normal,small,optional");
            Add("Коды восстановления", "Группировать секреты", "group_long_values", "bool", a => a.GroupLongValues.ToString(), (a,v) => a.GroupLongValues = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Коды восстановления", "Символов в группе", "group_size", "int", a => a.GroupSize.ToString(CultureInfo.InvariantCulture), (a,v) => a.GroupSize = ParseInt(v,a.GroupSize), null);
            Add("Коды восстановления", "Минимальная длина для групп", "group_min_length", "int", a => a.GroupMinLength.ToString(CultureInfo.InvariantCulture), (a,v) => a.GroupMinLength = ParseInt(v,a.GroupMinLength), null);
            Add("Коды восстановления", "Пробел между группами", "group_spaces", "double", a => a.GroupSpaces.ToString(CultureInfo.InvariantCulture), (a,v) => a.GroupSpaces = ParseDouble(v,a.GroupSpaces), null);
            Add("Коды восстановления", "Группы в пароле", "password_group_mode", "string", a => a.PasswordGroupMode.ToString(), (a,v) => a.PasswordGroupMode = v, "auto,always,never");
            Add("Иконки и цвета", "Обработка иконок", "entry_icon_print_mode", "string", a => a.EntryIconPrintMode.ToString(), (a,v) => a.EntryIconPrintMode = v, "original,auto,outline,silhouette,backdrop,none");
            Add("Две стороны", "Двусторонняя печать", "duplex_enabled", "bool", a => a.DuplexEnabled.ToString(), (a,v) => a.DuplexEnabled = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Две стороны", "Продолжение на обороте", "duplex_continuation", "bool", a => a.DuplexContinuation.ToString(), (a,v) => a.DuplexContinuation = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Две стороны", "Заполнять оборот", "duplex_back_fill", "string", a => a.DuplexBackFill.ToString(), (a,v) => a.DuplexBackFill = v, "design,blank");
            Add("Две стороны", "Разные записи на двух сторонах", "duplex_combine_different", "bool", a => a.DuplexCombineDifferent.ToString(), (a,v) => a.DuplexCombineDifferent = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("Две стороны", "Разделители надписи", "duplex_back_style", "string", a => a.DuplexBackStyle.ToString(), (a,v) => a.DuplexBackStyle = v, "minimal,rules");
            Add("Две стороны", "Рисунок оборота с данными", "duplex_back_background", "string", a => a.DuplexBackBackground.ToString(), (a,v) => a.DuplexBackBackground = v, "clean,watermark,gradient,diagonal,technical,texture");
            Add("Две стороны", "Язык оборота", "duplex_back_language", "string", a => a.DuplexBackLanguage.ToString(), (a,v) => a.DuplexBackLanguage = v, "ru,uk,en");
            Add("Две стороны", "Переворот листа", "duplex_flip", "string", a => a.DuplexFlip.ToString(), (a,v) => a.DuplexFlip = v, "long,short");
            Add("Две стороны", "Сдвиг оборота X, мм", "duplex_back_offset_x_mm", "double", a => a.DuplexBackOffsetXMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.DuplexBackOffsetXMm = ParseDouble(v,a.DuplexBackOffsetXMm), null);
            Add("Две стороны", "Сдвиг оборота Y, мм", "duplex_back_offset_y_mm", "double", a => a.DuplexBackOffsetYMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.DuplexBackOffsetYMm = ParseDouble(v,a.DuplexBackOffsetYMm), null);
            Add("QR-коды TOTP", "QR для TOTP", "totp_qr_enabled", "bool", a => a.TotpQrEnabled.ToString(), (a,v) => a.TotpQrEnabled = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase), null);
            Add("QR-коды TOTP", "Расположение QR", "totp_qr_position", "string", a => a.TotpQrPosition.ToString(), (a,v) => a.TotpQrPosition = v, "center,left,right");
            Add("QR-коды TOTP", "Размер QR, мм", "totp_qr_height_mm", "double", a => a.TotpQrHeightMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.TotpQrHeightMm = ParseDouble(v,a.TotpQrHeightMm), null);
            Add("QR-коды TOTP", "Минимум QR, мм", "totp_qr_min_height_mm", "double", a => a.TotpQrMinHeightMm.ToString(CultureInfo.InvariantCulture), (a,v) => a.TotpQrMinHeightMm = ParseDouble(v,a.TotpQrMinHeightMm), null);
            // Shared background: independently override front, data-back and empty-back.
            Add("Фон и декор", "Общий тип фона", "card_bg_pattern", "string", a=>a.CardBgPattern, (a,v)=>a.CardBgPattern=v, "clean,gradient,diagonal,technical,texture,watermark");
            Add("Фон и декор", "Общая серость, %", "card_bg_gray", "double", a=>a.CardBgGray.ToString(CultureInfo.InvariantCulture), (a,v)=>a.CardBgGray=Math.Clamp(ParseDouble(v,a.CardBgGray),0,100), null);
            Add("Фон и декор", "Контраст узора, %", "card_bg_contrast", "double", a=>a.CardBgContrast.ToString(CultureInfo.InvariantCulture), (a,v)=>a.CardBgContrast=Math.Clamp(ParseDouble(v,a.CardBgContrast),0,100), null);
            Add("Фон и декор", "Свой фон лица", "front_bg_override", "bool", a=>a.FrontBgOverride.ToString(), (a,v)=>a.FrontBgOverride=v.Equals("true",StringComparison.OrdinalIgnoreCase), null);
            Add("Фон и декор", "Фон лица", "front_bg_pattern", "string", a=>a.FrontBgPattern, (a,v)=>a.FrontBgPattern=v, "clean,gradient,diagonal,technical,texture,watermark");
            Add("Фон и декор", "Серость лица, %", "front_bg_gray", "double", a=>a.FrontBgGray.ToString(CultureInfo.InvariantCulture), (a,v)=>a.FrontBgGray=Math.Clamp(ParseDouble(v,a.FrontBgGray),0,100), null);
            Add("Фон и декор", "Контраст лица, %", "front_bg_contrast", "double", a=>a.FrontBgContrast.ToString(CultureInfo.InvariantCulture), (a,v)=>a.FrontBgContrast=Math.Clamp(ParseDouble(v,a.FrontBgContrast),0,100), null);

            Add("Две стороны", "Свой фон оборота с данными", "back_bg_override", "bool", a=>a.BackBgOverride.ToString(), (a,v)=>a.BackBgOverride=v.Equals("true",StringComparison.OrdinalIgnoreCase), null);
            Add("Две стороны", "Серость оборота, %", "back_bg_gray", "double", a=>a.BackBgGray.ToString(CultureInfo.InvariantCulture), (a,v)=>a.BackBgGray=Math.Clamp(ParseDouble(v,a.BackBgGray),0,100), null);
            Add("Две стороны", "Контраст оборота, %", "back_bg_contrast", "double", a=>a.BackBgContrast.ToString(CultureInfo.InvariantCulture), (a,v)=>a.BackBgContrast=Math.Clamp(ParseDouble(v,a.BackBgContrast),0,100), null);
            Add("Пустой оборот", "Фон пустого оборота", "empty_back_bg_source", "string", a=>a.EmptyBackBgSource, (a,v)=>a.EmptyBackBgSource=v, "back,front,custom,none");
            Add("Пустой оборот", "Свой фон пустого оборота", "empty_back_bg_pattern", "string", a=>a.EmptyBackBgPattern, (a,v)=>a.EmptyBackBgPattern=v, "clean,gradient,diagonal,technical,texture,watermark");
            Add("Пустой оборот", "Серость пустого оборота, %", "empty_back_bg_gray", "double", a=>a.EmptyBackBgGray.ToString(CultureInfo.InvariantCulture), (a,v)=>a.EmptyBackBgGray=Math.Clamp(ParseDouble(v,a.EmptyBackBgGray),0,100), null);
            Add("Пустой оборот", "Контраст пустого оборота, %", "empty_back_bg_contrast", "double", a=>a.EmptyBackBgContrast.ToString(CultureInfo.InvariantCulture), (a,v)=>a.EmptyBackBgContrast=Math.Clamp(ParseDouble(v,a.EmptyBackBgContrast),0,100), null);
            Add("Пустой оборот", "Шапка пустого оборота", "empty_back_show_header", "bool", a=>a.EmptyBackShowHeader.ToString(), (a,v)=>a.EmptyBackShowHeader=v.Equals("true",StringComparison.OrdinalIgnoreCase), null);
            Add("Пустой оборот", "Подвал пустого оборота", "empty_back_show_footer", "bool", a=>a.EmptyBackShowFooter.ToString(), (a,v)=>a.EmptyBackShowFooter=v.Equals("true",StringComparison.OrdinalIgnoreCase), null);
            Add("Пустой оборот", "Надпись на обороте", "empty_back_show_art", "bool", a=>a.EmptyBackShowArt.ToString(), (a,v)=>a.EmptyBackShowArt=v.Equals("true",StringComparison.OrdinalIgnoreCase), null);
            Add("Пустой оборот", "Иконка пустого оборота", "empty_back_show_icon", "bool", a=>a.EmptyBackShowIcon.ToString(), (a,v)=>a.EmptyBackShowIcon=v.Equals("true",StringComparison.OrdinalIgnoreCase), null);
            Add("Пустой оборот", "Рамка пустого оборота", "empty_back_show_frame", "bool", a=>a.EmptyBackShowFrame.ToString(), (a,v)=>a.EmptyBackShowFrame=v.Equals("true",StringComparison.OrdinalIgnoreCase), null);
            Add("Фоновая иконка", "Векторная иконка", "back_art_icon", "string", a=>a.BackArtIcon, (a,v)=>a.BackArtIcon=v, "none,shield,lock,key,card,vault");
            Add("Фоновая иконка", "Размер иконки, мм", "back_art_icon_size_mm", "double", a=>a.BackArtIconSizeMm.ToString(CultureInfo.InvariantCulture), (a,v)=>a.BackArtIconSizeMm=Math.Clamp(ParseDouble(v,a.BackArtIconSizeMm),4,28), null);
            Add("Фоновая иконка", "Цвет иконки (#RRGGBB)", "back_art_icon_color", "string", a=>a.BackArtIconColor, (a,v)=>a.BackArtIconColor=v, null);
            Add("Фоновая иконка", "Зазор под иконкой, мм", "back_art_text_gap_mm", "double", a=>a.BackArtTextGapMm.ToString(CultureInfo.InvariantCulture), (a,v)=>a.BackArtTextGapMm=Math.Clamp(ParseDouble(v,a.BackArtTextGapMm),2,12), null);
            Add("Фоновая иконка", "Видимость иконки, %", "back_art_icon_opacity", "double", a=>a.BackArtIconOpacity.ToString(CultureInfo.InvariantCulture), (a,v)=>a.BackArtIconOpacity=Math.Clamp(ParseDouble(v,a.BackArtIconOpacity),0,100), null);
            Add("Фоновая иконка", "Композиция по высоте", "back_art_icon_position", "string", a=>a.BackArtIconPosition, (a,v)=>a.BackArtIconPosition=v, "top,center,bottom");
            Add("Линии оборота", "Вариант линий", "back_rules_style", "string", a=>a.BackRulesStyle, (a,v)=>a.BackRulesStyle=v, "original,simple");
            Add("Линии оборота", "Ширина линий, мм", "back_rules_width_mm", "double", a=>a.BackRulesWidthMm.ToString(CultureInfo.InvariantCulture), (a,v)=>a.BackRulesWidthMm=Math.Clamp(ParseDouble(v,a.BackRulesWidthMm),8,55), null);
            Add("Линии оборота", "Толщина линий, мм", "back_rules_thickness_mm", "double", a=>a.BackRulesThicknessMm.ToString(CultureInfo.InvariantCulture), (a,v)=>a.BackRulesThicknessMm=Math.Clamp(ParseDouble(v,a.BackRulesThicknessMm),.05,1), null);
            Add("Линии оборота", "Отступ линий, мм", "back_rules_gap_mm", "double", a=>a.BackRulesGapMm.ToString(CultureInfo.InvariantCulture), (a,v)=>a.BackRulesGapMm=Math.Clamp(ParseDouble(v,a.BackRulesGapMm),.1,8), null);
            Add("Линии оборота", "Видимость линий, %", "back_rules_opacity", "double", a=>a.BackRulesOpacity.ToString(CultureInfo.InvariantCulture), (a,v)=>a.BackRulesOpacity=Math.Clamp(ParseDouble(v,a.BackRulesOpacity),0,100), null);
        if (result.Count != 140) throw new InvalidOperationException($"Appearance property registry out of sync: {result.Count} found, 140 expected.");
        return result;
    }
}
