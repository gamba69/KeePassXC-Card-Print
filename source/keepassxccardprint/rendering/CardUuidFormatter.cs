using KeePassXCCardPrint.Models;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.Rendering;

/// <summary>Only changes visual representation, never the stable full UUID used for records.</summary>
public static class CardUuidFormatter
{
    public static IReadOnlyDictionary<Guid, int> UniquePrefixLengths(
        IEnumerable<VaultRecord> records, int configuredLength)
    {
        int minimum = Math.Clamp(configuredLength, 4, 32);
        var uuids = records.Select(r => r.Uuid).Distinct().OrderBy(g => g.ToString("N"),
            StringComparer.Ordinal).ToArray();
        var texts = uuids.Select(g => g.ToString("N")).ToArray();
        var result = new Dictionary<Guid, int>();
        for (int i = 0; i < uuids.Length; i++)
        {
            int need = minimum;
            if (i > 0) need = Math.Max(need, CommonPrefix(texts[i - 1], texts[i]) + 1);
            if (i + 1 < texts.Length) need = Math.Max(need, CommonPrefix(texts[i], texts[i + 1]) + 1);
            result.Add(uuids[i], Math.Min(32, need));
        }
        return result;
    }

    private static int CommonPrefix(string x, string y)
    {
        int i = 0;
        while (i < Math.Min(x.Length, y.Length) && x[i] == y[i]) i++;
        return i;
    }

    public static string Format(Guid uuid, CardAppearance a, int? uniqueLength = null)
    {
        string raw = uuid.ToString("N");
        int length = Math.Clamp(uniqueLength ?? a.FooterUuidLength, 4, 32);
        raw = raw[..length];
        string value = a.FooterUuidSeparators switch
        {
            "every4" => JoinByFour(raw),
            "standard" => JoinStandard(raw),
            _ => raw
        };
        if (a.FooterUuidUppercase) value = value.ToUpperInvariant();
        if (a.FooterUuidPrefix) value = "ID: " + value;
        return value;
    }

    private static string JoinByFour(string raw) =>
        string.Join("-", Enumerable.Range(0, (raw.Length + 3) / 4)
            .Select(i => raw.Substring(i * 4, Math.Min(4, raw.Length - i * 4))));

    private static string JoinStandard(string raw)
    {
        if (raw.Length <= 8) return raw;
        var parts = new List<string> { raw[..8] };
        int index = 8;
        foreach (var count in new[] { 4, 4, 4, 12 })
        {
            if (index >= raw.Length) break;
            int n = Math.Min(count, raw.Length - index);
            parts.Add(raw.Substring(index, n));
            index += n;
        }
        return string.Join("-", parts);
    }
}
