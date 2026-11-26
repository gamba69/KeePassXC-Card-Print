using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using KeePassXCCardPrint.Rendering;
using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.ViewModels;

namespace KeePassXCCardPrint.Controls;

// Opt-in UI prose only: the source binding stays intact. Reuse the card
// engine's dictionary-aware line breaker, but measure in Avalonia DIPs with
// the same font as this control. No changes to vault data or printed cards.
public sealed class UiProseTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> SourceTextProperty =
        AvaloniaProperty.Register<UiProseTextBlock, string?>(nameof(SourceText));
    public static readonly StyledProperty<string> ProseLanguageProperty =
        AvaloniaProperty.Register<UiProseTextBlock, string>(nameof(ProseLanguage), "auto");

    static UiProseTextBlock() => AffectsMeasure<UiProseTextBlock>(SourceTextProperty, ProseLanguageProperty);
    public string? SourceText { get => GetValue(SourceTextProperty); set => SetValue(SourceTextProperty, value); }
    public string ProseLanguage { get => GetValue(ProseLanguageProperty); set => SetValue(ProseLanguageProperty, value); }
    protected override Type StyleKeyOverride => typeof(TextBlock);

    internal static string LanguageFor(string word, string paragraph, string locale)
        => HyphenationPackages.ResolveAuto(word, paragraph) ?? (Localizer.ResolveLanguage(locale) switch
        { "uk" => "uk", "en" => "en-us", _ => "ru" });

    protected override Size MeasureOverride(Size availableSize)
    {
        var source = SourceText ?? "";
        string display = source;
        double width = availableSize.Width;
        if (double.IsFinite(width) && width >= 8 && source.Length > 0)
        {
            var probe = new TextBlock
            {
                FontFamily = FontFamily, FontSize = FontSize, FontWeight = FontWeight,
                FontStyle = FontStyle, FontStretch = FontStretch,
                TextWrapping = TextWrapping.NoWrap, UseLayoutRounding = false
            };
            float Measure(string value)
            {
                probe.Text = value;
                probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return (float)probe.DesiredSize.Width;
            }
            try
            {
                // The UI ships these three dictionaries, so it remains usable
                // offline and independent of missing optional print packages.
                string locale = ProseLanguage == "auto" ? UiText.Language : ProseLanguage;
                display = string.Join("\n", ProseLayout.Break(source, (float)width, Measure,
                    (word, paragraph) => HyphenationPackages.Get("", LanguageFor(word, paragraph, locale), new()))
                    .Select(line => line.Text));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or FormatException)
            {
                // The normal Avalonia word-wrap remains available if a rule
                // file is missing or a very narrow viewport cannot fit a glyph.
                display = source;
            }
        }
        if (Text != display) SetCurrentValue(TextProperty, display);
        return base.MeasureOverride(availableSize);
    }
}
