using System.Globalization;

namespace KeePassXCCardPrint.Services;

/// <summary>Canonical numeric OpenType weight values.</summary>
public static class FontWeights
{
    public static readonly string[] Choices = ["200", "300", "400", "500", "600", "700", "800"];
    public static int Parse(string? value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
            return Math.Clamp(numeric, 200, 800);
        return 400;
    }
    public static string Normalize(string? weight)
    {
        int parsed=Parse(weight);
        return Choices.Select(x => int.Parse(x, CultureInfo.InvariantCulture)).OrderBy(x=>Math.Abs(x-parsed)).ThenBy(x=>x)
            .First().ToString(CultureInfo.InvariantCulture);
    }
    public static bool IsRecognized(string? weight) =>
        weight == "inherit" ||
        (int.TryParse(weight, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric)
         && numeric >= 200 && numeric <= 800);
    public static string Label(string code) => code switch
    {
        "200" => "ExtraLight (200)", "300" => "Light (300)",
        "400" => "Regular (400)", "500" => "Medium (500)",
        "600" => "SemiBold (600)", "700" => "Bold (700)",
        "800" => "ExtraBold (800)", _ => code
    };
}
