using SkiaSharp;

namespace KeePassXCCardPrint.Services;

/// <summary>Seven literal palette roles and one-level per-element color references.
/// The legacy default is evaluated by the renderer; an explicit reference always
/// uses the selected palette's exact RGB. No color settings contain vault data.</summary>
internal static class CardPalette
{
    internal const string DefaultSource = "inherit";
    internal const string Prefix = "palette:";
    internal static readonly string[] RoleKeys = [ "background_color", "ink_color", "muted_color", "accent_color", "header_color", "rule_color", "border_color" ];
    internal static bool IsRole(string key) => RoleKeys.Contains(key, StringComparer.Ordinal);
    internal static bool IsColorKey(string key) => IsRole(key) || Slots.ContainsKey(key);
    private sealed record Slot(string Ru,string Uk,string En,string Role);
    private static readonly Dictionary<string,Slot> Slots = new(StringComparer.Ordinal)
    {
        ["frame_color"] = new("Цвет рамки","Колір рамки","Border color","border_color"),
        ["title_color"] = new("Цвет заголовка","Колір заголовка","Title color","ink_color"),
        ["page_badge_text_color"] = new("Цвет номера","Колір номера","Number color","muted_color"),
        ["page_badge_background_color"] = new("Фон номера","Тло номера","Number background","header_color"),
        ["auth_badge_text_color"] = new("Цвет обозначений","Колір позначень","Indicator color","muted_color"),
        ["auth_badge_background_color"] = new("Фон обозначений","Тло позначень","Indicator background","header_color"),
        ["entry_icon_tint_color"] = new("Цвет иконки","Колір іконки","Icon color","ink_color"),
        ["entry_icon_background_color"] = new("Цвет подложки иконки","Колір підкладки іконки","Icon backdrop color","header_color"),
        ["header_fill_color"] = new("Цвет заливки шапки","Колір заливки шапки","Header fill color","header_color"),
        ["header_accent_color"] = new("Цвет акцентной полосы","Колір акцентної смуги","Header accent color","accent_color"),
        ["header_rule_color"] = new("Цвет линии шапки","Колір лінії шапки","Header rule color","rule_color"),
        ["field_label_color"] = new("Цвет названий полей","Колір назв полів","Field label color","muted_color"),
        ["field_value_color"] = new("Цвет значений","Колір значень","Value color","ink_color"),
        ["password_color"] = new("Цвет паролей","Колір паролів","Password color","ink_color"),
        ["notes_color"] = new("Цвет заметок","Колір нотаток","Notes color","ink_color"),
        ["section_heading_color"] = new("Цвет заголовков разделов","Колір заголовків розділів","Section heading color","accent_color"),
        ["field_rule_color"] = new("Цвет разделителей полей","Колір роздільників полів","Field divider color","rule_color"),
        ["footer_rule_color"] = new("Цвет линии подвала","Колір лінії колонтитула","Footer rule color","rule_color"),
        ["recovery_value_color"] = new("Цвет кодов","Колір кодів","Recovery code color","ink_color"),
        ["recovery_checkbox_color"] = new("Цвет флажков","Колір прапорців","Recovery checkbox color","muted_color"),
        ["otp_label_color"] = new("Цвет подписи OTP","Колір підпису OTP","OTP label color","muted_color"),
        ["otp_value_color"] = new("Цвет секрета OTP","Колір секрету OTP","OTP value color","ink_color"),
        ["otp_meta_color"] = new("Цвет параметров OTP","Колір параметрів OTP","OTP metadata color","muted_color"),
        ["back_caption_title_color"] = new("Цвет верхней надписи","Колір верхнього напису","Upper caption color","ink_color"),
        ["back_caption_subtitle_color"] = new("Цвет нижней надписи","Колір нижнього напису","Lower caption color","muted_color"),
        ["back_rules_color"] = new("Цвет линий оборота","Колір ліній звороту","Reverse rule color","rule_color"),
        ["front_background_color"] = new("Цвет фона лица","Колір тла лиця","Front background color","background_color"),
        ["back_background_color"] = new("Цвет фона оборота с данными","Колір тла звороту з даними","Data back background color","background_color"),
        ["empty_back_background_color"] = new("Цвет фона пустого оборота","Колір тла порожнього звороту","Empty back background color","background_color"),
        ["header_group_color"] = new("Цвет группы","Колір групи","Group color","muted_color"),
        ["footer_uuid_color"] = new("Цвет UUID","Колір UUID","UUID color","muted_color"),
        ["footer_date_color"] = new("Цвет даты","Колір дати","Date color","muted_color"),
        ["back_art_icon_color"] = new("Цвет иконки","Колір іконки","Emblem color","accent_color"),
    };
    internal static string RoleLabel(string key,string language) => key switch
    {
        "background_color" => TemplateEditorLayout.L(language,"Фон карточки","Тло картки","Card background"),
        "ink_color" => TemplateEditorLayout.L(language,"Основной текст","Основний текст","Primary text"),
        "muted_color" => TemplateEditorLayout.L(language,"Вторичный текст","Другорядний текст","Secondary text"),
        "accent_color" => TemplateEditorLayout.L(language,"Акцент","Акцент","Accent"),
        "header_color" => TemplateEditorLayout.L(language,"Заливка шапки","Заливка шапки","Header fill"),
        "rule_color" => TemplateEditorLayout.L(language,"Разделители","Роздільники","Dividers"),
        "border_color" => TemplateEditorLayout.L(language,"Рамка","Рамка","Border"),
        _ => key
    };
    internal static bool TryLabel(string key,string language,out string label)
    {
        if (IsRole(key)) { label=RoleLabel(key,language); return true; }
        if (Slots.TryGetValue(key,out var slot))
        { label=TemplateEditorLayout.L(language,slot.Ru,slot.Uk,slot.En); return true; }
        label=""; return false;
    }
    internal static string Help(string key,string label,string language) => label + "\n\n" + (IsRole(key)
        ? TemplateEditorLayout.L(language,
            "Общий цвет шаблона. Изменение перекрашивает все связанные элементы на обеих сторонах, но не меняет индивидуальные цвета. Выберите цвет через палитру, RGB или HEX.",
            "Спільний колір шаблону. Зміна перефарбовує всі пов'язані елементи на обох боках, але не змінює індивідуальні кольори. Оберіть колір через палітру, RGB або HEX.",
            "Shared template color. Changes update linked elements on both faces without modifying individual colors. Choose a color using the swatches, RGB or HEX.")
        : TemplateEditorLayout.L(language,
            "Цвет только этого элемента. Выберите общий цвет палитры, исходный цвет по умолчанию или индивидуальный HEX. Индивидуальный цвет не меняется при редактировании палитры. Настройки других элементов не изменяются.",
            "Колір лише цього елемента. Оберіть спільний колір палітри, початковий колір за замовчуванням або індивідуальний HEX. Індивідуальний колір не змінюється під час редагування палітри. Налаштування інших елементів не змінюються.",
            "Color of this element only. Choose a shared palette role, its original default, or an individual HEX color. Individual colors are unaffected by palette edits; other elements are unchanged."));

    internal static string RoleValue(CardAppearance a,string key) => key switch
    {
        "background_color" => a.BackgroundColor,
        "ink_color" => a.InkColor,
        "muted_color" => a.MutedColor,
        "accent_color" => a.AccentColor,
        "header_color" => a.HeaderColor,
        "rule_color" => a.RuleColor,
        "border_color" => a.BorderColor,
        _ => ""
    };
    private static SKColor Parse(string? value,SKColor fallback) =>
        !string.IsNullOrEmpty(value) && SKColor.TryParse(value,out var color) ? color : fallback;
    private static SKColor RoleFallback(string key) => key switch
    {
        "background_color" => Parse("#FFFFFF",SKColors.Black),
        "ink_color" => Parse("#1A2B40",SKColors.Black),
        "muted_color" => Parse("#53616E",SKColors.Black),
        "accent_color" => Parse("#36618D",SKColors.Black),
        "header_color" => Parse("#F4F7FA",SKColors.Black),
        "rule_color" => Parse("#CCD5E0",SKColors.Black),
        "border_color" => Parse("#C1C8D0",SKColors.Black),
        _ => SKColors.Black
    };
    internal static SKColor RoleColor(CardAppearance a,string key) => Parse(RoleValue(a,key),RoleFallback(key));
    internal static string Hex(SKColor c) => $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";
    internal static bool TryReference(string? source,out string key)
    {
        key=source is not null && source.StartsWith(Prefix,StringComparison.Ordinal) ? source[Prefix.Length..] : "";
        return IsRole(key);
    }
    internal static bool TryNormalize(string key,string? source,out string normalized)
    {
        normalized="";
        if (!IsColorKey(key) || source is null) return false;
        if (!IsRole(key) && (source==DefaultSource || TryReference(source,out _)))
        { normalized=source; return true; }
        if (source.Length==7 && source[0]=='#' && source.Skip(1).All(Uri.IsHexDigit) && SKColor.TryParse(source,out var color))
        { normalized=Hex(color); return true; }
        return false;
    }
    internal static SKColor Resolve(CardAppearance a,string? source,SKColor fallback)
    {
        if (TryReference(source,out var key)) return RoleColor(a,key);
        return Parse(source,fallback);
    }
    // Preserve the pre-10.1 style-specific rendering of inherited defaults.
    // Explicit palette references are never style-transformed.
    private static SKColor Styled(string? value,string original,string styled) =>
        string.Equals(value,original,StringComparison.OrdinalIgnoreCase) ? Parse(styled,SKColors.Black) : Parse(value,SKColors.Black);
    internal static SKColor FrontInk(CardAppearance a) => Styled(a.InkColor,"#1A2B40",a.Style switch {"technical"=>"#0C1420","minimal"=>"#202020","table"=>"#16283A",_=>"#1A2B40"});
    internal static SKColor FrontMuted(CardAppearance a) => Styled(a.MutedColor,"#53616E",a.Style=="minimal"?"#5B6067":"#53616E");
    internal static SKColor FrontAccent(CardAppearance a) => Styled(a.AccentColor,"#36618D",a.Style switch {"technical"=>"#242D36","minimal"=>"#4B5159","table"=>"#284C6B",_=>"#36618D"});
    internal static SKColor FrontRule(CardAppearance a) => Styled(a.RuleColor,"#CCD5E0",a.Style=="minimal"?"#DADCE0":"#CCD5E0");
    internal static SKColor HeaderFill(CardAppearance a) => Styled(a.HeaderColor,"#F4F7FA",a.Style switch {"classic"=>"#EFF1F4","table"=>"#EAF0F5",_=>"#F4F7FA"});
    internal static SKColor EditorDefault(CardAppearance a,string key) => key switch
    {
        "title_color" or "field_value_color" or "password_color" or "notes_color" or "recovery_value_color" or "otp_value_color" => FrontInk(a),
        "page_badge_text_color" or "auth_badge_text_color" or "header_group_color" or "field_label_color" or "recovery_checkbox_color" or "otp_label_color" or "otp_meta_color" => FrontMuted(a),
        "header_accent_color" or "section_heading_color" => FrontAccent(a),
        "header_rule_color" or "field_rule_color" => FrontRule(a),
        "header_fill_color" => HeaderFill(a),
        "page_badge_background_color" or "auth_badge_background_color" => Parse("#E5EBF2",SKColors.White),
        "entry_icon_tint_color" => Parse("#202B38",SKColors.Black),
        "entry_icon_background_color" => RoleColor(a,"header_color"),
        "back_rules_color" => Parse("#777777",SKColors.Black),
        "footer_uuid_color" or "footer_date_color" => Parse("#53616E",SKColors.Black),
        "back_art_icon_color" => RoleColor(a,"ink_color"),
        _ when Slots.TryGetValue(key,out var slot) => RoleColor(a,slot.Role),
        _ => RoleColor(a,key)
    };
    internal static string EditorHex(CardAppearance a,string key,string? source) =>
        Hex(IsRole(key) ? Parse(source,RoleFallback(key)) : Resolve(a,source,EditorDefault(a,key)));
    internal static string SourceCaption(string key,string? source,string language,string hex)
    {
        if (IsRole(key)) return hex;
        if (TryReference(source,out var role)) return RoleLabel(role,language);
        if (source==DefaultSource) return TemplateEditorLayout.L(language,"По умолчанию","За замовчуванням","Default");
        return hex;
    }
    internal static void LinkMatchingPresetColors(CardAppearance a)
    {
        // Built-in presets may have deliberately different accent shades.
        // Only equivalent colors become links; personal HEX overrides are untouched.
        string Link(string value,string role) =>
            TryNormalize(role,value,out var hex) && hex==Hex(RoleColor(a,role)) ? Prefix+role : value;
        a.HeaderGroupColor=Link(a.HeaderGroupColor,"muted_color");
        a.FooterUuidColor=Link(a.FooterUuidColor,"muted_color");
        a.FooterDateColor=Link(a.FooterDateColor,"muted_color");
        a.BackArtIconColor=Link(a.BackArtIconColor,"accent_color");
    }
    internal sealed record Scheme(string Ru,string Uk,string En,string[] Colors,string IconColorMode);
    internal static readonly Scheme[] Schemes =
    [
        new("Архивная","Архівна","Archive",["#FFFFFF","#1A2B40","#53616E","#36618D","#F4F7FA","#CCD5E0","#C1C8D0"],"original"),
        new("Монохромная","Монохромна","Monochrome",["#FFFFFF","#202020","#606060","#404040","#EEEEEE","#CCCCCC","#999999"],"gray"),
        new("Тёплая","Тепла","Warm",["#FFFCF3","#433529","#89745E","#986B3D","#EEE4D0","#D5C8B6","#B5A48C"],"palette:accent_color"),
        new("Техническая","Технічна","Technical",["#FFFFFF","#173B39","#54706B","#248578","#E6F1EF","#BED4CF","#92B4AE"],"palette:accent_color")
    ];
}
