using System.Text;
using System.Text.RegularExpressions;

namespace KeePassXCCardPrint.Services;

/// <summary>
/// Non-destructive display formatting for clearly international phone numbers.
/// No guesses about local dialing region; ambiguous identifiers are left intact.
/// </summary>
public static class PhoneLoginFormatter
{

    // Only the canonical, explicitly formatted phone output is grouped this way.
    // Separators are presentation boundaries, not printable space glyphs.
    // An ordinary username or email must never be split into pretend phone groups.
    public static string[]? PrintableGroups(string display)
    {
        if (Regex.IsMatch(display, @"\A\+380 \([0-9]{2}\) [0-9]{3} [0-9]{2} [0-9]{2}\z",
            RegexOptions.CultureInvariant))
            return display.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (Regex.IsMatch(display, @"\A\+1 \([0-9]{3}\) [0-9]{3}-[0-9]{4}\z",
            RegexOptions.CultureInvariant))
            return display.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return null;
    }

    public static string Format(string input, bool enabled)
    {
        if (!enabled || string.IsNullOrWhiteSpace(input)) return input;
        var source = input.Trim();
        if (source.Length > 40 || source[0] != '+') return input;
        var digits = new StringBuilder(source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            char ch = source[i];
            if (char.IsAsciiDigit(ch)) digits.Append(ch);
            else if (ch == '+' && i == 0) continue;
            else if (ch is not (' ' or '-' or '(' or ')' or '.')) return input;
        }
        var normalized = digits.ToString();
        if (normalized.Length == 12 && normalized.StartsWith("380", StringComparison.Ordinal))
            return $"+380 ({normalized[3..5]}) {normalized[5..8]} {normalized[8..10]} {normalized[10..12]}";
        if (normalized.Length == 11 && normalized[0] == '1' && normalized[1] is >= '2' and <= '9')
            return $"+1 ({normalized[1..4]}) {normalized[4..7]}-{normalized[7..11]}";
        return input;
    }
}
