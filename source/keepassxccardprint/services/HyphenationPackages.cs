using System.Collections.Concurrent;
using System.Net.Http;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meziantou.Framework;

namespace KeePassXCCardPrint.Services;

internal static class HyphenationPackages
{
    internal const string ConfigName = "hyphenation.json";
    internal const string CacheName = "hyphenation-cache";
    internal const string BundledRevision = "5684c0f51c0b81133db2efbe60a408b4155a3ff5";
    private const string Prefix = "hyph-utf8/tex/generic/hyph-utf8/patterns/";
    private const string Repository = "https://api.github.com/repos/hyphenation/tex-hyphen/";
    private const int MaxBytes = 4 * 1024 * 1024;
    internal sealed record Language(string Code, string Name, bool BuiltIn, int Left, int Right)
    { public override string ToString() => Name + " (" + Code + ")"; }
    // Offline snapshot of the upstream tree at BundledRevision, not an allow-list.
    // RefreshCatalogAsync replaces this snapshot with newly discovered packages.
    private const string InitialCodes = "af as be bg bn ca cop cs cu cy da de-1901 de-1996 de-ch-1901 el-monoton el-polyton en-gb en-us eo es et eu fi-x-school fi fr fur ga gl grc gu hi hr hsb hu hy ia id is it ka kk kmr kn la-x-classic la-x-liturgic la lt lv mk ml mn-cyrl mr mul-ethi nb nl nn oc or pa pi pl pms pt rm ro ru sa sh-cyrl sh-latn sk sl sq sr-cyrl sv ta te th tk tr uk zh-latn-pinyin";
    private const string CatalogName = "catalog.json";
    private const string AutoCheckName = "auto-check.txt";
    internal sealed record CatalogState(string Revision, DateTime CheckedUtc, Dictionary<string, string> Files);
    internal sealed record PackageInfo(Language Language, Pin? Pin, bool Ready, long Size, string Version,
        string License, DateTime? InstalledUtc, bool? UpdateAvailable, string? LatestRevision, DateTime? CheckedUtc,
        bool AvailableUpstream)
    {
        internal string Revision => Pin?.Revision ?? BundledRevision;
        internal bool NeedsRestore => Pin is not null && !Ready;
        internal bool CanUpdate => Ready && UpdateAvailable == true;
        internal bool CanRemoveOverride => Pin is not null;
    }
    internal static bool AutoCheck(string root) => !File.Exists(Path.Combine(root, CacheName, AutoCheckName));
    internal static void SetAutoCheck(string root, bool enabled)
    {
        string path = Path.Combine(root, CacheName, AutoCheckName);
        if (enabled) { if (File.Exists(path)) File.Delete(path); }
        else { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "disabled"); }
    }
    internal static CatalogState? ReadCatalogState(string root)
    {
        try
        {
            var path = CatalogPath(root);
            if (!File.Exists(path) || new FileInfo(path).Length > MaxBytes) return null;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            var data = doc.RootElement;
            string? revision = data.GetProperty("revision").GetString();
            if (!Hex(revision, 40) || !data.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Object) return null;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in files.EnumerateObject())
            {
                var sha = file.Value.GetString();
                if (!Hex(sha, 40) || !result.TryAdd(file.Name, sha!)) return null;
            }
            if (Discover(result).Length == 0) return null;
            return new(revision!, File.GetLastWriteTimeUtc(path), result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException) { return null; }
    }
    private static byte[] ReadBundled(string name)
    {
        using var input = Embedded(name);
        using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray();
    }
    internal static bool SameFiles(string code, IReadOnlyDictionary<string,string> files, string pattern, string? exceptions, string source) =>
        files.TryGetValue("txt/" + PatternName(code), out var p) && p == pattern &&
        files.TryGetValue("tex/" + SourceName(code), out var s) && s == source &&
        (files.TryGetValue("txt/" + ExceptionName(code), out var e) ? e : null) == exceptions;
    private static string HeaderValue(string source, string key)
    {
        foreach (string line in source.Split('\n'))
        {
            if (!line.StartsWith('%')) break;
            string value = line[1..].Trim();
            if (value.StartsWith(key + ":", StringComparison.Ordinal)) return value[(key.Length+1)..].Trim();
        }
        return "";
    }
    private static string LicenseName(string source)
    {
        bool license = false;
        foreach (string line in source.Split('\n'))
        {
            if (!line.StartsWith('%')) break;
            string v = line[1..].Trim();
            if (v.StartsWith("licence:", StringComparison.Ordinal) || v.StartsWith("license:", StringComparison.Ordinal)) { license = true; continue; }
            if (!license) continue;
            if (v.StartsWith("name:", StringComparison.Ordinal)) return v[5..].Trim();
            if (v.StartsWith("hyphenmins:", StringComparison.Ordinal) || v.StartsWith("texlive:", StringComparison.Ordinal)) break;
        }
        return "";
    }
    internal static PackageInfo[] Installed(string root)
    {
        var pins = Read(root);
        var catalog = ReadCatalogState(root);
        return CatalogFor(root).Where(l => l.BuiltIn || pins.ContainsKey(l.Code)).Select(lang =>
        {
            pins.TryGetValue(lang.Code, out var pin);
            bool ready = false; long size = 0; string version = "", license = "";
            DateTime? installed = null; bool? update = null;
            bool upstream = catalog is not null && catalog.Files.ContainsKey("txt/" + PatternName(lang.Code)) &&
                catalog.Files.ContainsKey("tex/" + SourceName(lang.Code));
            try
            {
                byte[] pattern, source; byte[]? exceptions;
                if (pin is null)
                {
                    pattern = ReadBundled(PatternName(lang.Code)); source = ReadBundled(SourceName(lang.Code));
                    exceptions = lang.Code == "uk" ? null : ReadBundled(ExceptionName(lang.Code));
                }
                else
                {
                    string folder = PathFor(root, lang.Code, pin);
                    pattern = ReadVerified(Path.Combine(folder, PatternName(lang.Code)), pin.Pattern);
                    source = ReadVerified(Path.Combine(folder, SourceName(lang.Code)), pin.Source);
                    exceptions = pin.Exceptions is null ? null : ReadVerified(Path.Combine(folder, ExceptionName(lang.Code)), pin.Exceptions);
                    installed = File.GetLastWriteTimeUtc(Path.Combine(folder, SourceName(lang.Code)));
                }
                size = pattern.LongLength + (exceptions?.LongLength ?? 0) + source.LongLength;
                var metadata = Encoding.UTF8.GetString(source);
                version = HeaderValue(metadata, "version"); license = LicenseName(metadata);
                ready = true;
                if (upstream) update = !SameFiles(lang.Code, catalog!.Files, GitBlob(pattern), exceptions is null ? null : GitBlob(exceptions), GitBlob(source));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or FormatException) { }
            if (version.Length == 0) version = (pin?.Revision ?? BundledRevision)[..12];
            return new PackageInfo(lang, pin, ready, size, version, license, installed, update, catalog?.Revision, catalog?.CheckedUtc, upstream);
        }).ToArray();
    }
    internal static bool ValidCode(string? code) => code is { Length: >= 2 and <= 64 } &&
        code[0] is >= 'a' and <= 'z' && code[^1] != '-' &&
        code.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') &&
        !code.Contains("--", StringComparison.Ordinal) && code is not ("auto" or "con" or "prn" or "aux" or "nul") &&
        !(code.Length == 4 && (code.StartsWith("com", StringComparison.Ordinal) || code.StartsWith("lpt", StringComparison.Ordinal)) && char.IsAsciiDigit(code[3]));
    private static bool IsBuiltIn(string code) => code is "ru" or "uk" or "en-us";
    private static Language Describe(string code)
    {
        string name = code switch
        {
            "ru" => "Русский", "uk" => "Українська", "en-us" => "English (US)", "en-gb" => "English (UK)",
            "de-1901" => "Deutsch (1901)", "de-1996" => "Deutsch (1996)", "de-ch-1901" => "Deutsch (Schweiz, 1901)",
            "el-monoton" => "Ελληνικά (monotonic)", "el-polyton" => "Ελληνικά (polytonic)",
            "fi-x-school" => "Suomi (school)", "la-x-classic" => "Latina (classical)", "la-x-liturgic" => "Latina (liturgical)",
            "zh-latn-pinyin" => "中文 (Pinyin)", _ => NativeName(code)
        };
        return new(code, name, IsBuiltIn(code), 2, code.StartsWith("en-", StringComparison.Ordinal) ? 3 : 2);
    }
    private static string NativeName(string code)
    {
        try { return CultureInfo.GetCultureInfo(code).NativeName; }
        catch (CultureNotFoundException) { return code; }
    }
    private static string CatalogPath(string root) => Path.Combine(root, CacheName, CatalogName);
    internal static Language[] CatalogFor(string root)
    {
        IEnumerable<string> codes = InitialCodes.Split(' ');
        try
        {
            var path = CatalogPath(root);
            if (File.Exists(path) && new FileInfo(path).Length <= MaxBytes)
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
                var data = doc.RootElement;
                if (!Hex(data.GetProperty("revision").GetString(), 40)) throw new InvalidDataException("Invalid catalog revision.");
                var cached = data.GetProperty("codes").EnumerateArray().Select(x => x.GetString()).ToArray();
                if (cached.Length == 0 || cached.Any(x => !ValidCode(x))) throw new InvalidDataException("Invalid catalog codes.");
                codes = cached.Select(x => x!);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException) { }
        IEnumerable<string> installed = [];
        try { installed = Read(root).Keys; }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException) { }
        return codes.Concat(installed).Concat(new[] { "ru", "uk", "en-us" }).Distinct(StringComparer.Ordinal)
            .Select(Describe).OrderByDescending(x => x.BuiltIn).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Code, StringComparer.Ordinal).ToArray();
    }
    internal static Language[] Discover(IReadOnlyDictionary<string, string> files) => files.Keys
        .Where(path => path.StartsWith("txt/hyph-", StringComparison.Ordinal) && path.EndsWith(".pat.txt", StringComparison.Ordinal))
        .Select(path => path[9..^8]).Where(ValidCode)
        .Where(code => files.ContainsKey("tex/" + SourceName(code)))
        .Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).Select(Describe).ToArray();
    internal sealed record Pin(string Revision, string Pattern, string? Exceptions, string Source);
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(45) };
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly ConcurrentDictionary<string, Lazy<Hyphenator>> Engines = new();
    static HyphenationPackages() => Client.DefaultRequestHeaders.UserAgent.ParseAdd("KeePassXC-Card-Print/10.2");

    internal static Language? Find(string code) => ValidCode(code) ? Describe(code) : null;
    private static bool Hex(string? value, int length) => value?.Length == length && value.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9');
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string PatternName(string code) => "hyph-" + code + ".pat.txt";
    private static string ExceptionName(string code) => "hyph-" + code + ".hyp.txt";
    private static string SourceName(string code) => "hyph-" + code + ".tex";
    private static string PathFor(string root, string code, Pin pin) => Path.Combine(root, CacheName, code, pin.Revision);
    internal static Dictionary<string, Pin> Read(string root)
    {
        string path = Path.Combine(root, ConfigName);
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException("Invalid hyphenation configuration size.");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return Parse(doc.RootElement);
    }
    internal static Dictionary<string, Pin> Parse(JsonElement json)
    {
        var pins = new Dictionary<string, Pin>(StringComparer.Ordinal);
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("version", out var v) || v.GetInt32() != 1 ||
            !json.TryGetProperty("packages", out var packages) || packages.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid hyphenation configuration.");
        foreach (var item in packages.EnumerateObject())
        {
            if (!ValidCode(item.Name) || item.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Invalid hyphenation package code.");
            var p = item.Value;
            var revision = p.GetProperty("revision").GetString();
            var pattern = p.GetProperty("pattern").GetString();
            var exceptions = p.GetProperty("exceptions").GetString();
            var source = p.GetProperty("source").GetString();
            if (!Hex(revision, 40) || !Hex(pattern, 64) || !Hex(source, 64) || (exceptions is not null && !Hex(exceptions, 64)) ||
                !pins.TryAdd(item.Name, new Pin(revision!, pattern!, exceptions, source!)))
                throw new InvalidDataException("Invalid hyphenation package version.");
        }
        return pins;
    }
    private static void Save(string root, Dictionary<string, Pin> pins)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, ConfigName);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temp))
            using (var writer = new Utf8JsonWriter(stream, new() { Indented = true }))
            {
                writer.WriteStartObject(); writer.WriteNumber("version", 1); writer.WriteStartObject("packages");
                foreach (var (code, pin) in pins.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    writer.WriteStartObject(code); writer.WriteString("revision", pin.Revision);
                    writer.WriteString("pattern", pin.Pattern); writer.WriteString("exceptions", pin.Exceptions);
                    writer.WriteString("source", pin.Source); writer.WriteEndObject();
                }
                writer.WriteEndObject(); writer.WriteEndObject();
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static string ChoicesFor(string selected)
    {
        Dictionary<string, Pin> pins;
        try { pins = Read(SettingsLocation.DirectoryPath); }
        catch (Exception) { pins = new(StringComparer.Ordinal); }
        return "auto," + string.Join(",", new[] { "ru", "uk", "en-us" }.Concat(pins.Keys.OrderBy(x => x, StringComparer.Ordinal))
            .Concat(ValidCode(selected) && selected != "auto" ? new[] { selected } : Array.Empty<string>()).Distinct(StringComparer.Ordinal));
    }
    internal static string? ResolveAuto(string word, string paragraph)
    {
        if (word.Any(c => "іїєґІЇЄҐ".Contains(c))) return "uk";
        if (word.Any(c => "ыэъёЫЭЪЁ".Contains(c))) return "ru";
        if (word.All(c => c is >= '\u0400' and <= '\u052f'))
        {
            bool uk = paragraph.Any(c => "іїєґІЇЄҐ".Contains(c));
            bool ru = paragraph.Any(c => "ыэъёЫЭЪЁ".Contains(c));
            return uk && !ru ? "uk" : ru && !uk ? "ru" : null;
        }
        return word.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z') ? "en-us" : null;
    }
    // TeX's source header declares typesetting minima. For legacy files with no
    // declaration use conservative minima; never infer them from the UI locale.
    internal static HyphenatorOptions Options(Language language, string? source = null)
    {
        int left = language.Left, right = language.Right;
        if (source is not null)
        {
            bool minima = false, typesetting = false;
            foreach (var line in source.Split('\n'))
            {
                if (!line.StartsWith('%')) { if (minima) break; continue; }
                var content = line[1..].Trim();
                if (content == "hyphenmins:") { minima = true; continue; }
                if (!minima || content.Length == 0) continue;
                if (content == "typesetting:") { typesetting = true; continue; }
                if (!typesetting) continue;
                var parts = content.Split(':', 2);
                if (parts.Length != 2 || parts[0] is not ("left" or "right")) break;
                if (!int.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value is < 1 or > 16) continue;
                if (parts[0] == "left") left = value; else right = value;
            }
        }
        return new() { LeftHyphenMin = left, RightHyphenMin = right };
    }
    private static Stream Embedded(string name) => typeof(HyphenationPackages).Assembly.GetManifestResourceStream("Hyphenation." + name)
        ?? throw new InvalidOperationException("Missing bundled hyphenation resource: " + name);
    private static Hyphenator LoadBundled(Language lang)
    {
        using var patterns = Embedded(PatternName(lang.Code));
        using var exceptions = lang.Code == "uk" ? null : Embedded(ExceptionName(lang.Code));
        return Hyphenator.Load(patterns, exceptions, Options(lang));
    }
    private static byte[] ReadVerified(string path, string hash)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaxBytes || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Missing or invalid hyphenation package file.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > MaxBytes || Hash(bytes) != hash) throw new InvalidDataException("Hyphenation package checksum mismatch.");
        return bytes;
    }
    private static Hyphenator LoadLocal(string root, Language lang, Pin pin)
    {
        var path = PathFor(root, lang.Code, pin);
        var pattern = ReadVerified(Path.Combine(path, PatternName(lang.Code)), pin.Pattern);
        var exceptions = pin.Exceptions is null ? [] : ReadVerified(Path.Combine(path, ExceptionName(lang.Code)), pin.Exceptions);
        var source = ReadVerified(Path.Combine(path, SourceName(lang.Code)), pin.Source);
        return Hyphenator.Parse(pattern.AsSpan(), exceptions.AsSpan(), Options(lang, Encoding.UTF8.GetString(source)));
    }
    internal static Hyphenator Get(string root, string code, Dictionary<string, Pin> pins)
    {
        var lang = Find(code) ?? throw new InvalidOperationException("Unknown hyphenation language: " + code);
        if (pins.TryGetValue(code, out var pin))
        {
            string key = root + "|" + code + "|" + pin.Revision + "|" + pin.Pattern + "|" + pin.Exceptions + "|" + pin.Source;
            try { return Engines.GetOrAdd(key, _ => new(() => LoadLocal(root, lang, pin))).Value; }
            catch (Exception ex) when (ex is IOException or FormatException or InvalidDataException)
            {
                Engines.TryRemove(key, out _);
                throw new InvalidOperationException("Hyphenation package " + code + " is unavailable. Restore it in Settings.", ex);
            }
        }
        if (!lang.BuiltIn) throw new InvalidOperationException("Install the hyphenation package " + code + " in Settings.");
        return Engines.GetOrAdd("bundled|" + code, _ => new(() => LoadBundled(lang))).Value;
    }
    private static async Task<byte[]> Download(string url, int limit, CancellationToken ct)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Hyphenation download exceeds limit.");
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream(); var buffer = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("Hyphenation download exceeds limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
    private static Task<byte[]> Raw(string revision, string path, CancellationToken ct) =>
        Download("https://raw.githubusercontent.com/hyphenation/tex-hyphen/" + revision + "/" + Prefix + path, MaxBytes, ct);
    private static string GitBlob(byte[] bytes)
    {
        // GitHub's Git object protocol uses SHA-1. Configuration pins use SHA-256.
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        digest.AppendData(Encoding.ASCII.GetBytes("blob " + bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0"));
        digest.AppendData(bytes);
        return Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
    }
    private static async Task<(string Revision, Dictionary<string, string> Files)> Latest(CancellationToken ct)
    {
        using var branch = JsonDocument.Parse(await Download(Repository + "branches/master", 1024 * 1024, ct).ConfigureAwait(false));
        string? revision = branch.RootElement.GetProperty("commit").GetProperty("sha").GetString();
        if (!Hex(revision, 40)) throw new InvalidDataException("Invalid upstream revision.");
        using var tree = JsonDocument.Parse(await Download(Repository + "git/trees/" + revision + "?recursive=1", 8 * MaxBytes, ct).ConfigureAwait(false));
        if (tree.RootElement.GetProperty("truncated").GetBoolean()) throw new InvalidDataException("Incomplete upstream catalog.");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in tree.RootElement.GetProperty("tree").EnumerateArray())
        {
            string? path = item.GetProperty("path").GetString();
            if (path is null || !path.StartsWith(Prefix, StringComparison.Ordinal) || item.GetProperty("type").GetString() != "blob") continue;
            string? sha = item.GetProperty("sha").GetString();
            if (Hex(sha, 40)) files.Add(path[Prefix.Length..], sha!);
        }
        return (revision!, files);
    }
    private static void SaveCatalog(string root, string revision, IReadOnlyDictionary<string, string> files)
    {
        var languages = Discover(files);
        if (languages.Length == 0)
            throw new InvalidDataException("Incomplete upstream hyphenation catalog.");
        var path = CatalogPath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temp))
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject(); writer.WriteString("revision", revision); writer.WriteStartArray("codes");
                foreach (var lang in languages) writer.WriteStringValue(lang.Code);
                writer.WriteEndArray(); writer.WriteStartObject("files");
                foreach (var lang in languages)
                    foreach (var relative in new[] { "txt/" + PatternName(lang.Code), "txt/" + ExceptionName(lang.Code), "tex/" + SourceName(lang.Code) })
                        if (files.TryGetValue(relative, out var blob)) writer.WriteString(relative, blob);
                writer.WriteEndObject(); writer.WriteEndObject();
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static async Task RefreshCatalogAsync(string root, CancellationToken ct = default)
    {
        // Catalog metadata is advisory; it must not block restoration of pinned packages.
        var (revision, files) = await Latest(ct).ConfigureAwait(false);
        SaveCatalog(root, revision, files);
    }
    private static void Publish(string root, Language lang, Pin pin, byte[] patterns, byte[]? exceptions, byte[] source)
    {
        if (Hash(patterns) != pin.Pattern || Hash(source) != pin.Source || (exceptions is null ? null : Hash(exceptions)) != pin.Exceptions)
            throw new InvalidDataException("Hyphenation package checksum mismatch.");
        // Parse before publishing any files or changing the portable configuration.
        var engine = Hyphenator.Parse(patterns.AsSpan(), (exceptions ?? []).AsSpan(), Options(lang, Encoding.UTF8.GetString(source)));
        if (engine.PatternCount == 0) throw new InvalidDataException("Empty hyphenation pattern set.");
        var target = PathFor(root, lang.Code, pin);
        string parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        string stage = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(stage);
        try
        {
            File.WriteAllBytes(Path.Combine(stage, PatternName(lang.Code)), patterns);
            if (exceptions is not null) File.WriteAllBytes(Path.Combine(stage, ExceptionName(lang.Code)), exceptions);
            File.WriteAllBytes(Path.Combine(stage, SourceName(lang.Code)), source);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(stage, target);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        Engines.Clear();
    }
    internal static async Task<bool> InstallOrUpdateAsync(string root, string code, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (revision, files) = await Latest(ct).ConfigureAwait(false);
            SaveCatalog(root, revision, files);
            return await InstallFromCatalogAsync(root, code, revision, files, ct).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }
    internal static async Task UpdateAllAsync(string root, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (revision, files) = await Latest(ct).ConfigureAwait(false);
            SaveCatalog(root, revision, files);
            var failures = new List<string>();
            foreach (var package in Installed(root).Where(p => p.UpdateAvailable == true))
            {
                ct.ThrowIfCancellationRequested();
                try { await InstallFromCatalogAsync(root, package.Language.Code, revision, files, ct).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException) { failures.Add(package.Language.Code); }
            }
            if (failures.Count != 0) throw new IOException(string.Join(", ", failures));
        }
        finally { Gate.Release(); }
    }
    private static async Task<bool> InstallFromCatalogAsync(string root, string code, string revision,
        Dictionary<string, string> files, CancellationToken ct)
    {
        var lang = Find(code) ?? throw new ArgumentException("Unknown language.", nameof(code));
        var before = Read(root);
        if (!Discover(files).Any(x => x.Code == code))
            throw new InvalidOperationException("This language is no longer in the upstream catalog; an existing pinned version can still be restored.");
        if (lang.BuiltIn && !before.ContainsKey(code))
        {
            var pattern = ReadBundled(PatternName(code)); var source = ReadBundled(SourceName(code));
            var exceptions = code == "uk" ? null : ReadBundled(ExceptionName(code));
            if (SameFiles(code, files, GitBlob(pattern), exceptions is null ? null : GitBlob(exceptions), GitBlob(source))) return false;
        }
        async Task<byte[]> Fetch(string relative)
        {
            if (!files.TryGetValue(relative, out var blob)) throw new InvalidDataException("Upstream package is incomplete.");
            var bytes = await Raw(revision, relative, ct).ConfigureAwait(false);
            if (GitBlob(bytes) != blob) throw new InvalidDataException("Upstream package verification failed.");
            return bytes;
        }
        var patternBytes = await Fetch("txt/" + PatternName(code)).ConfigureAwait(false);
        var exceptionPath = "txt/" + ExceptionName(code);
        var exceptionBytes = files.ContainsKey(exceptionPath) ? await Fetch(exceptionPath).ConfigureAwait(false) : null;
        var sourceBytes = await Fetch("tex/" + SourceName(code)).ConfigureAwait(false);
        var pin = new Pin(revision, Hash(patternBytes), exceptionBytes is null ? null : Hash(exceptionBytes), Hash(sourceBytes));
        var current = Read(root);
        if (!before.OrderBy(x => x.Key).SequenceEqual(current.OrderBy(x => x.Key)))
            throw new InvalidOperationException("Hyphenation configuration changed during download. Retry.");
        if (before.TryGetValue(code, out var previous) && previous.Pattern == pin.Pattern && previous.Exceptions == pin.Exceptions && previous.Source == pin.Source)
        {
            Publish(root, lang, previous, patternBytes, exceptionBytes, sourceBytes);
            return false;
        }
        Publish(root, lang, pin, patternBytes, exceptionBytes, sourceBytes);
        current[code] = pin;
        Save(root, current);
        return true;
    }
    internal static async Task RemoveAsync(string root, string code, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var pins = Read(root);
            if (pins.Remove(code)) Save(root, pins);
            Engines.Clear();
            // Cached versions are harmless and remain available for old snapshots; reset purges them.
        }
        finally { Gate.Release(); }
    }
    private static async Task RestoreOneAsync(string root, string code, Pin pin, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var lang = Describe(code);
        try { _ = LoadLocal(root, lang, pin); return; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException) { }
        var patterns = await Raw(pin.Revision, "txt/" + PatternName(code), ct).ConfigureAwait(false);
        var exceptions = pin.Exceptions is null ? null : await Raw(pin.Revision, "txt/" + ExceptionName(code), ct).ConfigureAwait(false);
        var source = await Raw(pin.Revision, "tex/" + SourceName(code), ct).ConfigureAwait(false);
        Publish(root, lang, pin, patterns, exceptions, source);
    }
    internal static async Task RestoreAsync(string root, string code, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try { if (Read(root).TryGetValue(code, out var pin)) await RestoreOneAsync(root, code, pin, ct).ConfigureAwait(false); }
        finally { Gate.Release(); }
    }
    internal static async Task ReconcileAsync(string root, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var (code, pin) in Read(root))
                await RestoreOneAsync(root, code, pin, ct).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }
}
