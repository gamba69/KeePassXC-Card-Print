using System.Security.Cryptography;
using KeePassXC.CloudTransport;
using KeePassXCCardPrint.Platforms;

namespace KeePassXCCardPrint.Services;

/// <summary>Owns current-format storage housekeeping and the explicit, full reset.</summary>
internal static class ConfigurationMaintenance
{
    private static readonly string[] SettingsFiles =
    [
        "ui-shell.json", "vault-state.json", "appearance.json",
        "domains.json", "appearance-template-source.json"
    ];
    private static readonly string[] SystemFiles =
    [
        "storage-mode.txt", "cloud-connection.json", "cloud-verified.json",
        "cloud-sync-journal.json",
        "cloud-sync-errors.log"
    ];
    internal const int RetainedBackups = 3;

    private static string[] Roots() =>
        [SettingsLocation.AppDataRoot, SettingsLocation.LocalRoot, SettingsLocation.CloudRoot];

    // At startup, only exact current-format configuration names are retained.
    // Files in print-tracking are deliberately opaque: keep the whole directory.
    internal static void CleanOnStartup()
    {
        foreach (var root in Roots().Distinct(StringComparer.OrdinalIgnoreCase))
            CleanRoot(root, fullReset: false, clearTracking: false,
                system: Path.GetFullPath(root).Equals(Path.GetFullPath(SettingsLocation.AppDataRoot),
                    StringComparison.OrdinalIgnoreCase));
        PruneBackups(SettingsResetService.BackupBase);
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Refusing to traverse a linked configuration path.");
    }

    private static void Remove(string path)
    {
        if (Directory.Exists(path))
        {
            RejectLink(path);
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            RejectLink(path);
            File.Delete(path);
        }
    }

    internal static void CleanRoot(string root, bool fullReset, bool clearTracking, bool system = false)
    {
        if (!Directory.Exists(root)) return;
        RejectLink(root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root).ToArray())
        {
            var name = Path.GetFileName(entry);
            if (!fullReset)
            {
                if (SettingsFiles.Contains(name, StringComparer.Ordinal) ||
                    (system && SystemFiles.Contains(name, StringComparer.Ordinal))) continue;
                if (name is "templates" or "print-tracking") continue;
                // All permanent snapshots live in the separate unified backup root.
                // Legacy per-storage backups are partial and must not survive.
                if (name == "backups") { Remove(entry); continue; }
                if (system && (name is "cloud-cache" or "updates")) continue;
            }
            else
            {
                if (!clearTracking && name == "print-tracking") continue;
                // Do not delete the updater's running resources during a reset.
                if (system && name == "updates") continue;
            }
            Remove(entry);
        }
    }

    // An archive is complete only after its full contents were copied, hashed,
    // and the manifest was written last. A timestamp alone is not proof.
    private const string CompleteMarker = "FULL-BACKUP.sha256";

    internal static void PruneBackups(string root)
    {
        if (!Directory.Exists(root)) return;
        RejectLink(root);
        var valid = new List<string>();
        foreach (var folder in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
        {
            RejectLink(folder);
            if (!ValidBackup(folder)) { Remove(folder); continue; }
            valid.Add(folder);
        }
        foreach (var old in valid.OrderByDescending(x => Path.GetFileName(x), StringComparer.Ordinal)
                     .Skip(RetainedBackups)) Remove(old);
    }

    private static bool ValidBackup(string folder)
    {
        var marker = Path.Combine(folder, CompleteMarker);
        if (!File.Exists(marker) || !new[] { "system", "local", "cloud-cache" }
                .All(name => Directory.Exists(Path.Combine(folder, name)))) return false;
        try
        {
            RejectLink(marker);
            var records = File.ReadAllLines(marker);
            if (records.Length == 0) return false;
            if (records.Length == 1 && records[0] == "# EMPTY")
                return !Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                    .Any(x => !x.Equals(marker, StringComparison.OrdinalIgnoreCase));
            var expected = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in records)
            {
                if (line.Length < 67 || line[64] != ' ' || line[65] != ' ') return false;
                var relative = line[66..];
                if (!expected.TryAdd(relative, line[..64])) return false;
            }
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(x => !x.Equals(marker, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (files.Length != expected.Count) return false;
            foreach (var file in files)
            {
                RejectLink(file);
                var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                if (!expected.TryGetValue(relative, out var digest) ||
                    !string.Equals(digest, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))),
                        StringComparison.Ordinal)) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return false; }
    }

    internal static bool IsVerifiedBackup(string folder)
    {
        try
        {
            return Directory.Exists(folder) &&
                (File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0 &&
                ValidBackup(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return false; }
    }

    internal static string NewBackupDirectory(string? baseDirectory = null)
    {
        var parent = baseDirectory ?? SettingsResetService.BackupBase;
        Directory.CreateDirectory(parent);
        // Seven fractional second digits make collisions vanishingly unlikely;
        // never reuse a folder if two operations happen at the same instant.
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fffffff");
        var path = Path.Combine(parent, stamp);
        if (Directory.Exists(path)) throw new IOException("Backup timestamp collision.");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CopyTree(string source, string destination)
    {
        RejectLink(source);
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            RejectLink(entry);
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry)) CopyTree(entry, target);
            else CopyChecked(entry, target);
        }
    }

    // Capture every application-owned file, including unknown legacy names.
    // Backups and updater binaries are not configuration and cannot be nested.
    private static void BackupRoot(string root, string backup, bool includeTracking, bool system = false)
    {
        Directory.CreateDirectory(backup);
        if (!Directory.Exists(root)) return;
        RejectLink(root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var name = Path.GetFileName(entry);
            if (name is "backups" or "updates" ||
                system && name == "cloud-cache" ||
                !includeTracking && name == "print-tracking") continue;
            RejectLink(entry);
            var target = Path.Combine(backup, name);
            if (Directory.Exists(entry)) CopyTree(entry, target);
            else CopyChecked(entry, target);
        }
    }

    internal static void SealBackup(string backup)
    {
        var marker = Path.Combine(backup, CompleteMarker);
        if (File.Exists(marker)) throw new IOException("Backup already sealed.");
        var files = Directory.EnumerateFiles(backup, "*", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var lines = new List<string>();
        foreach (var file in files)
        {
            RejectLink(file);
            var relative = Path.GetRelativePath(backup, file).Replace('\\', '/');
            lines.Add(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) + "  " + relative);
        }
        if (lines.Count == 0) lines.Add("# EMPTY");
        File.WriteAllLines(marker, lines);
        if (!ValidBackup(backup)) throw new IOException("Full backup verification failed.");
    }

    // Used by storage switching, remote application and mapping recovery.
    // Always a complete multi-root snapshot, never a partial per-operation copy.
    internal static string BackupPathForRoot(string root)
    {
        var full = Path.GetFullPath(root);
        var roots = Roots();
        var names = new[] { "system", "local", "cloud-cache" };
        for (var i = 0; i < roots.Length; i++)
            if (Path.GetFullPath(roots[i]).Equals(full, StringComparison.OrdinalIgnoreCase))
                return names[i];
        return "source";
    }

    internal static string CreateFullBackup(bool includeTracking = true, string? extraRoot = null)
    {
        var backup = NewBackupDirectory();
        try
        {
            var roots = Roots();
            var labels = new[] { "system", "local", "cloud-cache" };
            for (var i = 0; i < roots.Length; i++)
                BackupRoot(roots[i], Path.Combine(backup, labels[i]), includeTracking, system: i == 0);
            if (extraRoot is not null && BackupPathForRoot(extraRoot) == "source")
                BackupRoot(extraRoot, Path.Combine(backup, "source"), includeTracking);
            SealBackup(backup);
            PruneBackups(SettingsResetService.BackupBase);
            return backup;
        }
        catch
        {
            if (Directory.Exists(backup)) Remove(backup);
            throw;
        }
    }

    // Isolated mapping-recovery self-tests need full snapshots of the sandbox,
    // not the user's production configuration.
    internal static string CreateIsolatedFullBackup(string sourceRoot, string backupBase)
    {
        var backup = NewBackupDirectory(backupBase);
        try
        {
            foreach (var name in new[] { "system", "local", "cloud-cache" })
                Directory.CreateDirectory(Path.Combine(backup, name));
            BackupRoot(sourceRoot, Path.Combine(backup, "source"), includeTracking: true);
            SealBackup(backup);
            PruneBackups(backupBase);
            return backup;
        }
        catch
        {
            if (Directory.Exists(backup)) Remove(backup);
            throw;
        }
    }

    private static void CopyChecked(string src, string dest)
    {
        RejectLink(src);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(src, dest, overwrite: false);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(File.ReadAllBytes(src)),
                SHA256.HashData(File.ReadAllBytes(dest))))
            throw new IOException("Reset backup verification failed.");
    }

    private static void MergeTracking(string root, string target)
    {
        var source = Path.Combine(root, "print-tracking");
        if (!Directory.Exists(source)) return;
        RejectLink(source);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
        {
            RejectLink(file);
            var dest = Path.Combine(target, Path.GetFileName(file));
            if (File.Exists(dest))
            {
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(File.ReadAllBytes(file)),
                        SHA256.HashData(File.ReadAllBytes(dest))))
                    throw new IOException("Conflicting print-tracking files; reset cancelled without deleting originals.");
                continue;
            }
            CopyChecked(file, dest);
        }
    }

    /// <summary>Called by the worker only after the UI process has exited.
    /// A configured remote archive is reset and verified before local data is cleared.
    /// Never reports success if cloud cleanup failed.</summary>
    internal static async Task<SettingsResetResult> ResetAllAsync(bool clearTracking, CancellationToken ct = default)
    {
        var roots = Roots().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var backup = NewBackupDirectory();
        var scratch = Path.Combine(Path.GetTempPath(), "kpc-reset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var labels = new[] { "system", "local", "cloud-cache" };
            for (var i = 0; i < roots.Length; i++)
                BackupRoot(roots[i], Path.Combine(backup, labels[i]), includeTracking: !clearTracking);
            var prepared = Path.Combine(scratch, "clean");
            Directory.CreateDirectory(prepared);
            new UiSettingsStore(prepared).Save(new UiSettings());
            var combined = Path.Combine(prepared, "print-tracking");
            if (!clearTracking)
            {
                Directory.CreateDirectory(combined);
                foreach (var root in roots) MergeTracking(root, combined);
            }

            var profile = CloudConnectionStore.Load();
            if (profile.Provider.Length == 0 &&
                File.Exists(Path.Combine(SettingsLocation.AppDataRoot, "cloud-connection.json")))
                throw new InvalidDataException("Cloud profile is unreadable. Remote reset cannot be confirmed.");
            if (profile.Provider.Length != 0)
            {
                // Credentials remain usable until this operation has completed.
                // Never silently declare a partial (local-only) reset successful.
                using var transport = CloudTransportFactory.Create(
                    CloudTransportAdapter.Options(profile), CloudTransportAdapter.Services());
                if (!await transport.CheckConnectionAsync(ct))
                    throw new IOException("Cloud cannot be reached. No local settings have been reset.");
                var remotePath = CloudConnectionStore.RemoteFilePath(profile);
                var previous = Path.Combine(scratch, "previous.zip");
                if (await transport.DownloadAsync(remotePath, previous, ct))
                {
                    var stage = Path.Combine(scratch, "previous");
                    if (!clearTracking)
                    {
                        CloudConfigurationArchive.ReadVerified(previous, stage, validateConfiguration: false);
                        MergeTracking(stage, combined);
                        CopyChecked(previous, Path.Combine(backup, "remote", "configuration.zip"));
                    }
                }
                // Seal the complete snapshot BEFORE replacing any remote object.
                SealBackup(backup);
                var outgoing = Path.Combine(scratch, "clean.zip");
                var id = Guid.NewGuid();
                CloudConfigurationArchive.Create(prepared, outgoing, id, DateTime.UtcNow);
                await transport.EnsureDirectoryAsync(CloudConnectionStore.RemoteFolder(profile), ct);
                await transport.UploadAsync(outgoing, remotePath, CloudWriteMode.Replace, ct);
                var confirmed = Path.Combine(scratch, "confirmed.zip");
                if (!await transport.DownloadAsync(remotePath, confirmed, ct) ||
                    !CryptographicOperations.FixedTimeEquals(SHA256.HashData(File.ReadAllBytes(outgoing)),
                        SHA256.HashData(File.ReadAllBytes(confirmed))))
                    throw new IOException("Cloud reset verification failed; local settings were not removed.");
                var confirmedManifest = CloudConfigurationArchive.ReadVerified(confirmed,
                    Path.Combine(scratch, "confirmed"));
                if (confirmedManifest.SnapshotId != id)
                    throw new IOException("Cloud reset confirmed the wrong snapshot.");
            }

            if (profile.Provider.Length == 0) SealBackup(backup);

            // Only after a verified remote reset: purge every application root.
            // If not explicitly cleared, keep every original print-tracking directory
            // intact (not just its JSON files), including all nested contents.
            foreach (var root in roots.OrderByDescending(x => x.Length))
                CleanRoot(root, fullReset: true, clearTracking: clearTracking,
                    system: Path.GetFullPath(root).Equals(Path.GetFullPath(SettingsLocation.AppDataRoot),
                        StringComparison.OrdinalIgnoreCase));
            Directory.CreateDirectory(SettingsLocation.AppDataRoot);
            if (!clearTracking && Directory.Exists(combined) &&
                Directory.EnumerateFileSystemEntries(combined).Any())
            {
                var restored = Path.Combine(SettingsLocation.AppDataRoot, "print-tracking");
                Directory.CreateDirectory(restored);
                foreach (var src in Directory.EnumerateFiles(combined, "*", SearchOption.TopDirectoryOnly))
                {
                    var dest = Path.Combine(restored, Path.GetFileName(src));
                    if (File.Exists(dest))
                    {
                        if (!CryptographicOperations.FixedTimeEquals(
                                SHA256.HashData(File.ReadAllBytes(src)),
                                SHA256.HashData(File.ReadAllBytes(dest))))
                            throw new IOException("Conflicting print-tracking files; original directory remains intact.");
                    }
                    else CopyChecked(src, dest);
                }
            }
            SettingsLocation.Activate(ConfigurationStorage.AppData);
            new UiSettingsStore().Save(new UiSettings());
            Directory.Delete(scratch, recursive: true);
            if (clearTracking)
            {
                // Explicit tracking deletion must not leave recoverable tracking
                // copies in the application's older reset backups.
                foreach (var old in Directory.EnumerateDirectories(SettingsResetService.BackupBase))
                    if (!Path.GetFullPath(old).Equals(Path.GetFullPath(backup),
                            StringComparison.OrdinalIgnoreCase)) Remove(old);
            }
            PruneBackups(SettingsResetService.BackupBase);
            return new SettingsResetResult(backup, Directory.EnumerateFiles(backup, "*",
                SearchOption.AllDirectories).Count());
        }
        catch
        {
            // Only a sealed, verified snapshot is eligible for retention.
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
            catch (IOException) { }
            if (Directory.Exists(backup) && !ValidBackup(backup)) Remove(backup);
            throw;
        }
    }
}
