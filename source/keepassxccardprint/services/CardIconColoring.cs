using SkiaSharp;

namespace KeePassXCCardPrint.Services;

/// <summary>Entry icon coloring is independent of shape processing. A scheme sets
/// only the shared policy; an explicit entry policy always takes priority.</summary>
internal static class CardIconColoring
{
    internal static string SharedOptions => "original,gray," + string.Join(",",CardPalette.RoleKeys.Select(k=>CardPalette.Prefix+k));
    internal static string EntryOptions => "inherit," + SharedOptions + ",custom";
    internal static bool IsModeKey(string? key) => key is "palette_icon_color_mode" or "entry_icon_color_mode";
    internal static string NormalizeShared(string? mode) => (mode is "original" or "gray") || CardPalette.TryReference(mode,out _) ? mode! : "original";
    internal static string NormalizeEntry(string? mode) => mode is "inherit" or "custom" ? mode! :
        (mode is "original" or "gray") || CardPalette.TryReference(mode,out _) ? mode! : "inherit";
    internal static string EffectiveMode(CardAppearance a)
    {
        string mode=NormalizeEntry(a.EntryIconColorMode);
        return mode=="inherit" ? NormalizeShared(a.PaletteIconColorMode) : mode;
    }
    internal static void SetEntryMode(CardAppearance a,string mode)
    {
        a.EntryIconColorMode=NormalizeEntry(mode);
        // Individual mode is a literal RGB override, never a hidden palette link.
        if(a.EntryIconColorMode=="custom")
            a.EntryIconTintColor=CardPalette.EditorHex(a,"entry_icon_tint_color",a.EntryIconTintColor);
    }
    internal static bool ShowTint(CardAppearance a) => a.EntryIconMode!="none" && a.EntryIconPrintMode!="none" &&
        (EffectiveMode(a)=="custom" || (EffectiveMode(a)=="original" && a.EntryIconPrintMode is ("outline" or "silhouette")));
    internal readonly record struct Resolved(string Mode,SKColor Tint);
    internal static Resolved Resolve(CardAppearance a)
    {
        string mode=EffectiveMode(a);
        var tint=mode=="custom" ? CardPalette.Resolve(a,a.EntryIconTintColor,new SKColor(32,43,56)) :
            CardPalette.TryReference(mode,out var role) ? CardPalette.RoleColor(a,role) : SKColors.Black;
        return new(mode,tint);
    }
    // Integer BT.709-style luminance; alpha and the processed shape are preserved.
    private static byte Luma(SKColor c) => (byte)((54*c.Red+183*c.Green+19*c.Blue+128)>>8);
    internal static SKColor Apply(SKColor color,string shape,Resolved coloring)
    {
        if(color.Alpha==0 || coloring.Mode=="original")return color;
        if(coloring.Mode=="gray")
        {
            byte gray=Luma(color);
            return new SKColor(gray,gray,gray,color.Alpha);
        }
        if(shape is "outline" or "silhouette")return coloring.Tint.WithAlpha(color.Alpha);
        // Keep tonal details in opaque/multicolor bitmaps. Black maps to the
        // chosen tint and white stays white; all intermediate tones are related
        // to that tint instead of collapsing into a flat opaque square.
        int tone=Luma(color);
        byte Mix(byte component)=>(byte)(component+((255-component)*tone+127)/255);
        return new SKColor(Mix(coloring.Tint.Red),Mix(coloring.Tint.Green),Mix(coloring.Tint.Blue),color.Alpha);
    }
    internal static string ModeLabel(string code,string language)
    {
        string L(string ru,string uk,string en)=>TemplateEditorLayout.L(language,ru,uk,en);
        if(CardPalette.TryReference(code,out var key))return CardPalette.RoleLabel(key,language);
        return code switch
        {
            "inherit"=>L("По общей схеме","За спільною схемою","Use shared scheme"),
            "original"=>L("Оригинальные цвета","Оригінальні кольори","Original colors"),
            "gray"=>L("Оттенки серого","Відтінки сірого","Grayscale"),
            "custom"=>L("Индивидуальный цвет","Індивідуальний колір","Individual color"),
            _=>L("Оригинальные цвета","Оригінальні кольори","Original colors")
        };
    }
    internal static string FieldLabel(string key,string language)=>key=="palette_icon_color_mode" ?
        TemplateEditorLayout.L(language,"Окраска иконок","Забарвлення іконок","Entry icon coloring") :
        TemplateEditorLayout.L(language,"Окраска","Забарвлення","Coloring");
    internal static string Help(string key,string label,string language)=>label+"\n\n"+(key=="palette_icon_color_mode" ?
        TemplateEditorLayout.L(language,
            "Общая окраска иконок записей. Применяется к иконкам, наследующим цветовую схему. Меняет цвета, но не форму иконки. Готовые схемы устанавливают этот режим автоматически.",
            "Спільне забарвлення іконок записів. Застосовується до іконок, що успадковують колірну схему. Змінює кольори, але не форму іконки. Готові схеми задають цей режим автоматично.",
            "Shared coloring for entry icons that follow the color scheme. Changes colors, not the icon shape. Ready-made schemes set this policy automatically.") :
        TemplateEditorLayout.L(language,
            "Наследуйте общую схему или задайте оригинальные цвета, оттенки серого, цвет палитры либо индивидуальный RGB/HEX. Настройка имеет приоритет над общей схемой и не изменяет способ обработки иконки.",
            "Успадковуйте спільну схему або задайте оригінальні кольори, відтінки сірого, колір палітри чи індивідуальний RGB/HEX. Налаштування має пріоритет над спільною схемою і не змінює спосіб обробки іконки.",
            "Follow the shared scheme or choose original colors, grayscale, a palette role or individual RGB/HEX. This setting overrides the shared scheme without changing icon processing."));
}
