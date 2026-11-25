using System.Globalization;
using System.Text;
using KeePassXCCardPrint.Services;
using Meziantou.Framework;

namespace KeePassXCCardPrint.Rendering;

internal static class ProseLayout
{
    internal readonly record struct Line(string Text, bool ParagraphFinal);
    internal static List<Line> Wrap(string source, float width, float size, string family, string weight,
        float tracking, bool hyphenate, string language)
    {
        string root = hyphenate ? SettingsLocation.DirectoryPath : "";
        var pins = hyphenate ? HyphenationPackages.Read(root) : new Dictionary<string, HyphenationPackages.Pin>();
        var engines = new Dictionary<string, Hyphenator>(StringComparer.Ordinal);
        if (hyphenate && language != "auto") engines[language] = HyphenationPackages.Get(root, language, pins);
        Hyphenator? Engine(string word, string paragraph)
        {
            if (!hyphenate) return null;
            var code = language == "auto" ? HyphenationPackages.ResolveAuto(word, paragraph) : language;
            if (code is null) return null;
            if (!engines.TryGetValue(code, out var engine)) engines[code] = engine = HyphenationPackages.Get(root, code, pins);
            return engine;
        }
        float Measure(string s) => CardTextMetrics.MeasureMm(s, size, family, weight, tracking);
        return Break(source, width, Measure, Engine);
    }
    internal static List<Line> Break(string source, float width, Func<string, float> measure, Func<string, string, Hyphenator?> engine)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var result = new List<Line>();
        Span<int> positions = stackalloc int[128];
        foreach (string paragraph in source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (paragraph.Length == 0) { result.Add(new("", true)); continue; }
            int[] boundaries = [.. StringInfo.ParseCombiningCharacters(paragraph), paragraph.Length];
            int start = 0;
            while (start < boundaries.Length - 1)
            {
                int end = start, space = -1;
                while (end < boundaries.Length - 1)
                {
                    string candidate = paragraph[boundaries[start]..boundaries[end+1]];
                    if (measure(candidate) > width) break;
                    end++;
                    if (char.IsWhiteSpace(paragraph[boundaries[end-1]]) && paragraph[boundaries[end-1]] is not '\u00a0' and not '\u202f') space = end;
                }
                if (end == start) throw new InvalidOperationException("A single text element does not fit the card.");
                if (end == boundaries.Length - 1 || paragraph.AsSpan(boundaries[end]).Trim().IsEmpty)
                { result.Add(new(paragraph[boundaries[start]..].TrimEnd(), true)); break; }
                // Never introduce hyphens into URLs, emails, codes, or mixed alphanumeric tokens.
                int tokenStart = boundaries[end], tokenEnd = boundaries[end];
                while (tokenStart > 0 && !char.IsWhiteSpace(paragraph[tokenStart-1])) tokenStart--;
                while (tokenEnd < paragraph.Length && !char.IsWhiteSpace(paragraph[tokenEnd])) tokenEnd++;
                int wordEnd = tokenEnd;
                while (wordEnd > tokenStart && paragraph[wordEnd-1] is '.' or ',' or ':' or ';' or '!' or '?' or ')') wordEnd--;
                int wordStart = tokenStart;
                while (wordStart < wordEnd && paragraph[wordStart] is '(' or '«' or '“') wordStart++;
                string word = paragraph[wordStart..wordEnd];
                int hyphen = -1;
                if (word.Length is >= 5 and <= 120 && IsProseWord(word) && engine(word, paragraph) is { } hyphenator)
                {
                    int count = hyphenator.Hyphenate(word, positions);
                    for (int i = Math.Min(count, positions.Length)-1; i >= 0; i--)
                    {
                        int p = wordStart + positions[i];
                        if (p <= boundaries[start] || p > boundaries[end] || (space > start && p <= boundaries[space])) continue;
                        int index = Array.BinarySearch(boundaries, p);
                        if (index < 0 || measure(paragraph[boundaries[start]..p] + "-") > width) continue;
                        hyphen = index; break;
                    }
                }
                if (hyphen > start)
                { result.Add(new(paragraph[boundaries[start]..boundaries[hyphen]] + "-", false)); start = hyphen; }
                else
                {
                    int cut = space > start ? space : end;
                    string text = paragraph[boundaries[start]..boundaries[cut]].TrimEnd();
                    // Avoid a blank visual line caused solely by leading whitespace.
                    if (text.Length == 0 && cut < boundaries.Length - 1) { start = cut; continue; }
                    result.Add(new(text, cut == boundaries.Length - 1)); start = cut;
                }
            }
        }
        return result;
    }
    internal static bool IsProseWord(string word)
    {
        // A word in an uncased script is valid prose as well. Combining marks
        // are retained; the shaping and hyphenation paths guard grapheme breaks.
        bool hasLetter = false, hasLower = false, hasUpper = false;
        int index = 0;
        foreach (var rune in word.EnumerateRunes())
        {
            if (Rune.IsLetter(rune))
            {
                hasLetter = true;
                if (Rune.IsUpper(rune)) { if (index != 0) return false; hasUpper = true; }
                if (Rune.IsLower(rune)) hasLower = true;
            }
            else if (Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))
                return false;
            index++;
        }
        return hasLetter && (!hasUpper || hasLower);
    }
    internal static int ExpandableSpaces(string text) => text.Trim().Count(c => c == ' ');
    internal static float Offset(string alignment, float available, float measured) => alignment switch
    { "center" => Math.Max(0, available - measured) / 2, "right" => Math.Max(0, available - measured), _ => 0 };
}
