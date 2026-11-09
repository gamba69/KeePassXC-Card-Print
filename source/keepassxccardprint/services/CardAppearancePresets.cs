namespace KeePassXCCardPrint.Services;

/// <summary>
/// Five built-in card styles.
/// Every built-in preset is a complete, immutable face + reverse appearance.
/// Applying a built-in never inherits decoration, duplex, or typography from
/// the previously selected template. Users must create a personal copy to edit.
/// </summary>
public static class CardAppearancePresets
{
    public static CardAppearance Create(string style)
    {
        if (style is not ("archive" or "technical" or "classic" or "minimal" or "table"))
            throw new ArgumentException("Unknown built-in card style.", nameof(style));

        // A preset must not depend on the previously edited card. In particular,
        // the reverse-side header, footer, background and emblem are part of it.
        var result = new CardAppearance { Style = style };
        switch (style)
        {
            case "technical":
                result.HeaderFill = false;
                result.HeaderAccent = true;
                result.AccentColor = "#242D36";
                result.InkColor = "#182029";
                result.RuleColor = "#A1A9B2";
                result.HeaderColor = "#FFFFFF";
                break;
            case "minimal":
                result.HeaderFill = false;
                result.HeaderAccent = false;
                result.SectionRules = false;
                result.Frame = false;
                result.InkColor = "#202020";
                break;
            case "classic":
                result.HeaderColor = "#EFF1F4";
                break;
            case "table":
                result.SectionRules = true;
                result.LabelSizePt = 6.3;
                break;
        }
        return result;
    }
}
