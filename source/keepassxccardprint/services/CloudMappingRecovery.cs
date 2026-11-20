using System.Text;
using System.Text.Json;
using KeePassXCCardPrint.Platforms;

namespace KeePassXCCardPrint.Services;

/// <summary>
/// Recover a damaged cloud-cache field mapping only after an explicit user decision.
/// The original file is kept outside the synchronized allow-list before any replacement.
/// </summary>
internal static class CloudMappingRecovery
{
    private const long MaxMappingSize = 2_000_000;
    internal static string ReviewMarkerPath => Path.Combine(SettingsLocation.AppDataRoot, "cloud-mapping-review-pending.flag");
    internal static bool ReviewRequired => File.Exists(ReviewMarkerPath);
    internal static void ClearReviewRequired() { if (File.Exists(ReviewMarkerPath)) File.Delete(ReviewMarkerPath); }

    internal static bool IsValidMapping(byte[] content)
    {
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 64 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var field in document.RootElement.EnumerateObject())
                if (field.Value.ValueKind != JsonValueKind.Object) return false;
            var mapping = JsonSerializer.Deserialize(content,
                VaultJsonContext.Default.DictionaryStringFieldRule);
            return mapping is not null && mapping.Values.All(rule => rule is not null);
        }
        catch (JsonException) { return false; }
    }

    internal static bool IsValidMappingFile(string file)
    {
        try
        {
            if (!File.Exists(file) ||
                (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 ||
                new FileInfo(file).Length > MaxMappingSize) return false;
            return IsValidMapping(File.ReadAllBytes(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return false; }
    }

    internal static bool NeedsRecovery(string root)
    {
        var file = Path.Combine(root, "mapping.json");
        return File.Exists(file) && !IsValidMappingFile(file);
    }

    // A failed transition may leave the active source incompatible while the
    // cloud cache is still valid, or it may leave BOTH roots damaged. Always
    // repair the active source first to prevent copying the same invalid JSON
    // back to the cache on the next attempt to switch storage.
    internal static string? FindProblemRoot(string activeRoot, string cloudRoot)
    {
        if (NeedsRecovery(activeRoot)) return activeRoot;
        return NeedsRecovery(cloudRoot) ? cloudRoot : null;
    }

    /// <summary>Looks only in active roots and verified full configuration snapshots.</summary>
    internal static string? FindPreviousMapping(string damagedRoot, IEnumerable<string> knownRoots)
    {
        var damaged = Path.GetFullPath(Path.Combine(damagedRoot, "mapping.json"));
        var candidates = new List<string>();
        foreach (var root in knownRoots)
        {
            try { candidates.Add(Path.Combine(Path.GetFullPath(root), "mapping.json")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        if (PlatformServices.IsInitialized && Directory.Exists(SettingsResetService.BackupBase))
        {
            foreach (var folder in Directory.EnumerateDirectories(SettingsResetService.BackupBase,
                         "*", SearchOption.TopDirectoryOnly))
            {
                if (!ConfigurationMaintenance.IsVerifiedBackup(folder)) continue;
                foreach (var name in new[] { "system", "local", "cloud-cache" })
                    candidates.Add(Path.Combine(folder, name, "mapping.json"));
            }
        }
        return candidates.Where(p => !string.Equals(Path.GetFullPath(p), damaged,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            .Where(IsValidMappingFile)
            .OrderByDescending(p => HasRules(p))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static bool HasRules(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            return doc.RootElement.EnumerateObject().Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return false; }
    }

    /// <summary>
    /// Always preserve the original outside cloud-cache before replacing it.
    /// A null source means an explicitly confirmed reset to an empty rule dictionary.
    /// </summary>
    internal static string Restore(string damagedRoot, string backupRoot, string? source,
        string? reviewMarker = null)
    {
        var target = Path.Combine(damagedRoot, "mapping.json");
        if (!File.Exists(target) || !NeedsRecovery(damagedRoot))
            throw new InvalidOperationException("No incompatible mapping is present.");
        if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Cannot replace a linked mapping file.");
        var original = File.ReadAllBytes(target);
        var replacement = source is null ? Encoding.UTF8.GetBytes("{}") : File.ReadAllBytes(source);
        if (!IsValidMapping(replacement))
            throw new InvalidDataException("Mapping recovery source is not a valid mapping file.");
        var production = PlatformServices.IsInitialized &&
            Path.GetFullPath(backupRoot).Equals(
                Path.GetFullPath(Path.Combine(SettingsLocation.AppDataRoot, "backups")),
                StringComparison.OrdinalIgnoreCase);
        var fullBackup = production
            ? ConfigurationMaintenance.CreateFullBackup(extraRoot: damagedRoot)
            : ConfigurationMaintenance.CreateIsolatedFullBackup(damagedRoot, backupRoot);
        var label = production ? ConfigurationMaintenance.BackupPathForRoot(damagedRoot) : "source";
        var backup = Path.Combine(fullBackup, label, "mapping.json");
        if (!File.Exists(backup) || !File.ReadAllBytes(backup).AsSpan().SequenceEqual(original))
            throw new IOException("Mapping recovery full backup verification failed.");

        // A restart must not automatically publish a recovered or empty mapping.
        // The marker is outside the synchronized allow-list and is removed only
        // after an explicitly approved, successful synchronization.
        var marker = reviewMarker ?? ReviewMarkerPath;
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
        var temporary = Path.Combine(damagedRoot, ".mapping-recovery-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, replacement);
            if (!IsValidMappingFile(temporary))
                throw new InvalidDataException("Mapping recovery staging validation failed.");
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return backup;
    }
}
