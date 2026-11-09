using KeePassXCCardPrint.Models;
using System.Text.RegularExpressions;

namespace KeePassXCCardPrint.Services;

/// <summary>
/// Selects a printable address without modifying the KDBX record. KeePassXC
/// browser-integration additional URLs may be URL match patterns: wildcard
/// schemes, subdomains or paths. The domain in such a pattern is meaningful for
/// printing, even though the entire pattern is not a navigable URL.
/// </summary>
public sealed record UrlLookupResult(string Url, string Source, int AdditionalIndex,
    bool HasPrimary, int AdditionalRecognized, int AdditionalCandidates);

public static class UrlSourceSelector
{
    private static readonly Regex AdditionalKey = new(
        @"^KP2A_URL(?:_[0-9]+)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsAdditionalUrlAttribute(string key) => AdditionalKey.IsMatch(key);

    public static int AdditionalUrlOrder(string key)
    {
        if (key.Equals("KP2A_URL", StringComparison.OrdinalIgnoreCase)) return 0;
        var match = Regex.Match(key, @"^KP2A_URL_(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int n) && n < 9_998) return n + 1;
        return 10_000;
    }

    /// <summary>
    /// Converts browser URL match patterns to a safe, printable site address.
    /// A wildcard path never becomes part of the printed value. For a wildcard
    /// subdomain, print only the explicitly specified domain suffix; for a
    /// wildcard scheme, use https solely to make host parsing consistent.
    /// Keep an ordinary concrete URL intact (apart from adding https to a
    /// scheme-less URL). Passwords/user-info, placeholders and arbitrary
    /// schemes are rejected. No network requests are made.
    /// </summary>
    public static string NormalizePrintableUrl(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return "";
        var source = candidate.Trim();
        // KeePassXC browser-integration entries may quote an entire URL to
        // request exact matching. Quotes are matching syntax, not part of the
        // address to print. Only remove balanced outer delimiters; malformed
        // or embedded quotes must not be silently reinterpreted as a host.
        if (source.Length >= 2)
        {
            char first = source[0], last = source[^1];
            if ((first == '"' && last == '"') ||
                (first == '\u201C' && last == '\u201D') ||
                (first == '\u00AB' && last == '\u00BB') ||
                (first == '\'' && last == '\''))
                source = source[1..^1].Trim();
        }
        if (source.Length == 0 || source.Length > 2048 || source.Any(char.IsWhiteSpace) ||
            source.Any(char.IsControl) || source.IndexOfAny(['{', '}', '\\', '"', '\u201C', '\u201D', '\u00AB', '\u00BB']) >= 0)
            return "";

        int schemeEnd = source.IndexOf("://", StringComparison.Ordinal);
        bool wildcardScheme = false;
        if (schemeEnd >= 0)
        {
            string scheme = source[..schemeEnd];
            if (scheme == "*")
            {
                wildcardScheme = true;
                source = "https" + source[schemeEnd..];
            }
            else if (!scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                     !scheme.Equals("https", StringComparison.OrdinalIgnoreCase) &&
                     !scheme.Equals("ftp", StringComparison.OrdinalIgnoreCase)) return "";
        }
        else
        {
            // Bare hosts and host:port forms are common in entry attributes;
            // Uri validation below rejects unsupported schemes and invalid ports.
            source = "https://" + source;
        }

        int authorityStart = source.IndexOf("://", StringComparison.Ordinal) + 3;
        int authorityEnd = source.IndexOfAny(['/', '?', '#'], authorityStart);
        if (authorityEnd < 0) authorityEnd = source.Length;
        string authority = source[authorityStart..authorityEnd];
        if (authority.Length == 0 || authority.Contains('@')) return "";
        bool wildcardHost = authority.StartsWith("*.", StringComparison.Ordinal);
        if (wildcardHost) authority = authority[2..];
        if (authority.Length == 0 || authority.Contains('*')) return "";

        string normalized = source[..authorityStart] + authority + source[authorityEnd..];
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https" or "ftp") ||
            uri.UserInfo.Length != 0 || string.IsNullOrWhiteSpace(uri.Host) ||
            !(uri.Host.Contains('.', StringComparison.Ordinal) ||
              uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)) return "";

        // A browser pattern is not an exact page URL. Its domain is reliable,
        // but its path and protocol cannot be promised. Print just the site.
        bool pattern = wildcardHost || wildcardScheme || source[authorityEnd..].Contains('*');
        if (pattern) return uri.Scheme + "://" + uri.Authority;
        return normalized;
    }

    public static bool IsConcreteUrl(string? candidate) => NormalizePrintableUrl(candidate).Length != 0;

    public static string[] ExtractAdditionalUrls(IEnumerable<KeyValuePair<string, string>> fields) =>
        fields.Where(kv => IsAdditionalUrlAttribute(kv.Key))
            .OrderBy(kv => AdditionalUrlOrder(kv.Key))
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => NormalizePrintableUrl(kv.Value))
            .Where(value => value.Length != 0)
            // Keep each accepted KP2A_URL entry: distinct exact-match URLs
            // can normalize to the same printable domain, but diagnostics
            // must count both as recognized (e.g. 2/2, not 1/2).
            .Take(50).ToArray();

    /// <summary>Shared lookup used by both card layout and the source diagnostic.
    /// The preview never receives a different source decision from printing.</summary>
    public static UrlLookupResult ResolveDetailed(VaultRecord record, string? mode)
    {
        var primary = NormalizePrintableUrl(record.Url);
        var additional = record.AdditionalUrls.Select(NormalizePrintableUrl)
            .Where(url => url.Length != 0).ToArray();
        string url = "", source = "none";
        int index = 0;
        if (mode != "extra" && primary.Length != 0)
        {
            url = primary;
            source = "main";
        }
        else if (mode != "main" && additional.Length != 0)
        {
            url = additional[0];
            source = "extra";
            index = 1;
        }
        return new UrlLookupResult(url, source, index, primary.Length != 0,
            additional.Length, Math.Max(record.AdditionalUrlCandidateCount, record.AdditionalUrls.Length));
    }

    public static string Resolve(VaultRecord record, string? mode) => ResolveDetailed(record, mode).Url;

    /// <summary>Secure explanation: only presence, counts and the print gate;
    /// no URLs, record names, database paths or credentials are disclosed.</summary>
    public static string ExplainForMapping(VaultRecord? record, string? mode,
        string visibility, bool templatePrintUrl, string language)
    {
        string lang = language is "en" or "uk" ? language : "ru";
        if (record is null) return lang switch
        {
            "en" => "Open a database to check this field in the card preview.",
            "uk" => "Відкрийте базу, щоб перевірити джерело URL на картці.",
            _ => "Откройте базу, чтобы проверить источник URL на карточке."
        };
        var result = ResolveDetailed(record, mode);
        var count = result.AdditionalCandidates;
        var valid = result.AdditionalRecognized;
        string summary = lang switch
        {
            "en" => $"Primary: {(result.HasPrimary ? "yes" : "no")} · Extra recognized: {valid}/{count}.",
            "uk" => $"Основний: {(result.HasPrimary ? "є" : "немає")} · Додаткових розпізнано: {valid}/{count}.",
            _ => $"Основной: {(result.HasPrimary ? "есть" : "нет")} · Дополнительных распознано: {valid}/{count}."
        };
        string reason;
        if (!templatePrintUrl)
            reason = lang switch
            {
                "en" => "URL printing is disabled in the appearance template.",
                "uk" => "Друк URL вимкнено в шаблоні оформлення.",
                _ => "Печать URL выключена в шаблоне оформления."
            };
        else if (visibility == "hidden")
            reason = lang switch
            {
                "en" => "Mapping visibility is set to Never print.",
                "uk" => "У маппінгу вибрано «Не друкувати».",
                _ => "В маппинге выбрано «Не печатать»."
            };
        else if (result.Url.Length != 0)
            reason = result.Source switch
            {
                "main" => lang switch { "en" => "Will print the primary URL.", "uk" => "Буде надруковано основний URL.", _ => "Будет напечатан основной URL." },
                _ => lang switch { "en" => "Will print an additional URL.", "uk" => "Буде надруковано додатковий URL.", _ => "Будет напечатан дополнительный URL." }
            };
        else if (mode == "main")
            reason = lang switch { "en" => "Primary-only mode: no usable primary URL.", "uk" => "Лише основний: придатного URL немає.", _ => "Только основной: подходящего URL нет." };
        else if (mode == "extra")
            reason = lang switch { "en" => "Additional-only mode: no usable extra URL.", "uk" => "Лише додатковий: придатного URL немає.", _ => "Только дополнительные: подходящего URL нет." };
        else
            reason = lang switch { "en" => "Neither source contains a usable URL.", "uk" => "Немає придатного URL в жодному джерелі.", _ => "Ни в одном источнике нет подходящего URL." };
        return summary + " " + reason;
    }
}
