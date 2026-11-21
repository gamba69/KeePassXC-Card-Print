using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeePassXCCardPrint.Services;

// The archive is an allow-list of portable configuration, NEVER a dump of the data directory.
public sealed class CloudPortableUi
{
    public string Language { get; set; } = "auto";
    public string Theme { get; set; } = "auto";
}

public sealed class CloudArchiveFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class CloudArchiveManifest
{
    public int FormatVersion { get; set; } = 1;
    public Guid SnapshotId { get; set; }
    public DateTime SavedAtUtc { get; set; }
    public List<CloudArchiveFile> Files { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CloudArchiveManifest))]
[JsonSerializable(typeof(CloudPortableUi))]
internal partial class CloudArchiveJsonContext : JsonSerializerContext { }

public static class CloudConfigurationArchive
{
    public const string RemotePath = "KeePassXC-Card-Print/configuration.zip";
    public const long MaxArchiveBytes = 32 * 1024 * 1024;
    private const long MaxExtractedBytes = 64 * 1024 * 1024;
    private const long MaxFileBytes = 4 * 1024 * 1024;
    private const int MaxFiles = 2000;
    private static readonly string[] FlatFiles =
        ["appearance.json", "appearance-template-source.json", "domains.json"];

    public static bool IsPortableChange(string name) => name == "ui-shell.json" || PortablePath(name);

    private static bool PortablePath(string name)
    {
        if (name == "portable-ui.json" || FlatFiles.Contains(name, StringComparer.Ordinal)) return true;
        if (name.StartsWith("templates/", StringComparison.Ordinal))
        {
            var leaf = name[10..];
            return SafeJsonLeaf(leaf);
        }
        if (name.StartsWith("print-tracking/", StringComparison.Ordinal))
        {
            var leaf = name[15..];
            return leaf.Length == 69 && leaf.EndsWith(".json", StringComparison.Ordinal) &&
                leaf[..64].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
        }
        return false;
    }

    private static bool SafeJsonLeaf(string leaf)
    {
        if (!leaf.EndsWith(".json", StringComparison.Ordinal)) return false;
        var name = leaf[..^5];
        return name.Length is >= 1 and <= 64 &&
            name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_') &&
            !new[] { "CON", "PRN", "AUX", "NUL" }.Contains(name, StringComparer.OrdinalIgnoreCase) &&
            !((name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
              name.Length == 4 && char.IsDigit(name[3]));
    }

    private static void CheckFile(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Configuration contains a link.");
        if (new FileInfo(path).Length > MaxFileBytes)
            throw new InvalidDataException("Configuration file exceeds the cloud limit.");
    }

    private static SortedDictionary<string, byte[]> Collect(string root)
    {
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var ui = new UiSettingsStore(root).Load();
        files["portable-ui.json"] = JsonSerializer.SerializeToUtf8Bytes(
            new CloudPortableUi { Language = ui.Language, Theme = ui.Theme },
            CloudArchiveJsonContext.Default.CloudPortableUi);
        foreach (var name in FlatFiles)
        {
            var path = Path.Combine(root, name);
            if (!File.Exists(path)) continue;
            CheckFile(path);
            files[name] = File.ReadAllBytes(path);
        }
        foreach (var directory in new[] { "templates", "print-tracking" })
        {
            var folder = Path.Combine(root, directory);
            if (!Directory.Exists(folder)) continue;
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Configuration contains a linked folder.");
            foreach (var path in Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
            {
                var name = directory + "/" + Path.GetFileName(path);
                if (!PortablePath(name)) throw new InvalidDataException("Unsafe configuration file name.");
                CheckFile(path);
                files.Add(name, File.ReadAllBytes(path));
            }
        }
        if (files.Count > MaxFiles || files.Values.Sum(x => (long)x.Length) > MaxExtractedBytes)
            throw new InvalidDataException("Configuration exceeds the cloud limit.");
        foreach (var pair in files) ValidateJson(pair.Key, pair.Value);
        return files;
    }

    // Validates a pending local snapshot before any remote configuration object
    // can be written. This must not repair or discard unknown mapping formats.
    public static void ValidateLocal(string root) => _ = Collect(root);

    private static void ValidateJson(string name, byte[] content)
    {
        using var doc = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 64 });
        if (name == "appearance-template-source.json")
        {
            if (doc.RootElement.ValueKind != JsonValueKind.String ||
                doc.RootElement.GetString() is not { } filename ||
                !SafeJsonLeaf(filename))
                throw new InvalidDataException("Invalid active appearance template.");
            return;
        }
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid configuration JSON: " + name);
        if (name == "appearance.json")
        {
            if (JsonSerializer.Deserialize(content, CardAppearanceJsonContext.Default.CardAppearance) is null)
                throw new InvalidDataException("Invalid card appearance.");
        }
        else if (name == "domains.json")
        {
            var domains = JsonSerializer.Deserialize(content, DomainPrefixContext.Default.DomainPrefixFile)
                ?? throw new InvalidDataException("Invalid domain rules.");
            if (domains.Format != "KeePassCardPrint-auth-subdomains-v1")
                throw new InvalidDataException("Unknown domain rule format.");
            DomainPrefixStore.Normalize(domains.Prefixes ?? []);
        }
        else if (name.StartsWith("templates/", StringComparison.Ordinal))
        {
            if (!doc.RootElement.TryGetProperty("format", out var format) ||
                format.GetString() != "KeePassCardPrint-template-v1" ||
                !doc.RootElement.TryGetProperty("settings", out var settings) ||
                settings.ValueKind != JsonValueKind.Object ||
                settings.Deserialize(CardAppearanceJsonContext.Default.CardAppearance) is null)
                throw new InvalidDataException("Invalid print template.");
            try { _ = CardAppearanceStore.ParseMapping(doc.RootElement); }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidDataException)
            { throw new InvalidDataException("Invalid embedded template mapping.", ex); }
        }
        else if (name.StartsWith("print-tracking/", StringComparison.Ordinal))
        {
            var tracking = JsonSerializer.Deserialize(content, PrintTrackingJsonContext.Default.PrintTrackingData)
                ?? throw new InvalidDataException("Invalid print-tracking data.");
            if (tracking.Version != 1 || tracking.Entries is null || tracking.History is null)
                throw new InvalidDataException("Unsupported print-tracking format.");
        }
        if (name == "portable-ui.json")
        {
            var value = JsonSerializer.Deserialize(content, CloudArchiveJsonContext.Default.CloudPortableUi)
                ?? throw new InvalidDataException("Missing portable UI settings.");
            if (value.Language is not ("auto" or "ru" or "uk" or "en") ||
                value.Theme is not ("auto" or "light" or "dark"))
                throw new InvalidDataException("Invalid portable UI settings.");
        }
    }

    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    public static string Signature(string root)
    {
        var files = Collect(root);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (name, content) in files)
            sha.AppendData(Encoding.UTF8.GetBytes(name + ":" + Hash(content) + "\n"));
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    public static CloudArchiveManifest Create(string root, string zipPath, Guid id, DateTime savedAtUtc)
    {
        var files = Collect(root);
        var manifest = new CloudArchiveManifest { SnapshotId = id, SavedAtUtc = savedAtUtc };
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using (var stream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
        {
            foreach (var (name, content) in files)
            {
                manifest.Files.Add(new CloudArchiveFile { Path = name, Size = content.LongLength, Sha256 = Hash(content) });
                using var entry = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                entry.Write(content);
            }
            using var meta = zip.CreateEntry("manifest.json", CompressionLevel.Optimal).Open();
            JsonSerializer.Serialize(meta, manifest, CloudArchiveJsonContext.Default.CloudArchiveManifest);
        }
        if (new FileInfo(zipPath).Length > MaxArchiveBytes)
            throw new InvalidDataException("Cloud archive exceeds the size limit.");
        return manifest;
    }

    // Parses and validates EVERY entry before touching the destination.
    public static CloudArchiveManifest ReadVerified(string zipPath, string stageRoot,
        bool validateConfiguration = true)
    {
        if (new FileInfo(zipPath).Length > MaxArchiveBytes)
            throw new InvalidDataException("Cloud archive exceeds the size limit.");
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count is < 2 or > MaxFiles + 1)
            throw new InvalidDataException("Invalid archive entry count.");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            if ((name != "manifest.json" && !PortablePath(name)) ||
                name.Contains('\\') || name.Contains("..", StringComparison.Ordinal) ||
                name.StartsWith('/') ||
                entry.Length < 0 || entry.Length > MaxFileBytes || !entries.TryAdd(name, entry))
                throw new InvalidDataException("Unsafe or duplicate archive entry.");
            total = checked(total + entry.Length);
            if (total > MaxExtractedBytes)
                throw new InvalidDataException("Archive extraction limit exceeded.");
        }
        if (!entries.TryGetValue("manifest.json", out var metadata))
            throw new InvalidDataException("Missing cloud manifest.");
        CloudArchiveManifest manifest;
        using (var input = metadata.Open())
            manifest = JsonSerializer.Deserialize(input, CloudArchiveJsonContext.Default.CloudArchiveManifest)
                ?? throw new InvalidDataException("Invalid cloud manifest.");
        if (manifest.FormatVersion != 1 || manifest.SnapshotId == Guid.Empty ||
            manifest.SavedAtUtc.Kind != DateTimeKind.Utc ||
            manifest.SavedAtUtc > DateTime.UtcNow.AddMinutes(5) ||
            manifest.Files.Count != entries.Count - 1)
            throw new InvalidDataException("Unsupported cloud manifest.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Directory.CreateDirectory(stageRoot);
        foreach (var file in manifest.Files)
        {
            if (!PortablePath(file.Path) || !seen.Add(file.Path) ||
                !entries.TryGetValue(file.Path, out var entry) || entry.Length != file.Size ||
                file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Cloud manifest does not match archive entries.");
            var relative = file.Path.Replace('/', Path.DirectorySeparatorChar);
            var destination = Path.Combine(stageRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using (var input = entry.Open())
            using (var output = File.Create(destination))
            {
                input.CopyTo(output);
                if (output.Length != entry.Length)
                    throw new InvalidDataException("Incomplete archive entry.");
            }
            var data = File.ReadAllBytes(destination);
            if (!CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(data), Convert.FromHexString(file.Sha256)))
                throw new InvalidDataException("Cloud SHA-256 verification failed.");
            if (validateConfiguration || file.Path.StartsWith("print-tracking/", StringComparison.Ordinal))
                ValidateJson(file.Path, data);
        }
        if (!seen.Contains("portable-ui.json"))
            throw new InvalidDataException("Missing portable UI configuration.");
        return manifest;
    }

    // Local machine-only preferences are preserved by merging just language/theme.
    // Replacement is transactional, including removals of no-longer-present templates.
    public static void ApplyVerified(string destinationRoot, string stageRoot,
        CloudArchiveManifest manifest, string? backupBase = null)
    {
        var originals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in FlatFiles)
            if (File.Exists(Path.Combine(destinationRoot, name))) originals.Add(name);
        foreach (var directory in new[] { "templates", "print-tracking" })
        {
            var folder = Path.Combine(destinationRoot, directory);
            if (Directory.Exists(folder))
            {
                if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked configuration folder.");
                foreach (var file in Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
                {
                    var name = directory + "/" + Path.GetFileName(file);
                    if (PortablePath(name)) originals.Add(name);
                }
            }
        }
        if (File.Exists(Path.Combine(destinationRoot, "ui-shell.json")))
        {
            CheckFile(Path.Combine(destinationRoot, "ui-shell.json"));
            originals.Add("ui-shell.json");
        }
        var newNames = manifest.Files.Select(x => x.Path)
            .Where(x => x != "portable-ui.json").ToHashSet(StringComparer.Ordinal);
        var touched = originals.Union(newNames).Where(x => x != "ui-shell.json").ToArray();
        if (Directory.Exists(destinationRoot) &&
            (File.GetAttributes(destinationRoot) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked configuration root.");
        foreach (var name in touched)
        {
            var parent = Path.GetDirectoryName(name.Replace('/', Path.DirectorySeparatorChar));
            if (parent is { Length: > 0 })
            {
                var dir = Path.Combine(destinationRoot, parent);
                if (Directory.Exists(dir) &&
                    (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked configuration folder.");
            }
            var target = Path.Combine(destinationRoot, name.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(target)) CheckFile(target);
        }
        if (backupBase is null) ConfigurationMaintenance.CreateFullBackup();
        // The rollback is transient, never a permanent partial backup.
        var rollback = Path.Combine(Path.GetTempPath(), "kpc-cloud-rollback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rollback);
        var changed = new List<string>();
        try
        {
            foreach (var name in originals)
            {
                var source = Path.Combine(destinationRoot, name.Replace('/', Path.DirectorySeparatorChar));
                CheckFile(source);
                var target = Path.Combine(rollback, name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target);
            }
            foreach (var name in touched)
            {
                var target = Path.Combine(destinationRoot, name.Replace('/', Path.DirectorySeparatorChar));
                changed.Add(name);
                if (newNames.Contains(name))
                {
                    var staged = Path.Combine(stageRoot, name.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(staged, target, overwrite: true);
                }
                else if (File.Exists(target)) File.Delete(target);
            }
            var ui = JsonSerializer.Deserialize(
                File.ReadAllBytes(Path.Combine(stageRoot, "portable-ui.json")),
                CloudArchiveJsonContext.Default.CloudPortableUi)!;
            var store = new UiSettingsStore(destinationRoot);
            var current = store.Load();
            current.Language = ui.Language;
            current.Theme = ui.Theme;
            changed.Add("ui-shell.json");
            store.Save(current);
        }
        catch
        {
            for (var i = changed.Count - 1; i >= 0; --i)
            {
                var name = changed[i];
                var target = Path.Combine(destinationRoot, name.Replace('/', Path.DirectorySeparatorChar));
                var saved = Path.Combine(rollback, name.Replace('/', Path.DirectorySeparatorChar));
                if (originals.Contains(name)) File.Copy(saved, target, overwrite: true);
                else if (File.Exists(target)) File.Delete(target);
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(rollback)) Directory.Delete(rollback, recursive: true);
        }
    }
}
