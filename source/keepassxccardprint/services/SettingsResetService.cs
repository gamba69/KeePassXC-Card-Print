using System.Security.Cryptography;
using System.Text;

namespace KeePassXCCardPrint.Services;

/// <summary>Options must be explicitly confirmed by a user. False preserves print history.</summary>
public readonly record struct SettingsResetOptions(bool AllStorages, bool ClearPrintHistory);
public readonly record struct SettingsResetResult(string BackupDirectory, int FileCount);
public readonly record struct SettingsResetOutcome(bool Success, string Message);

/// <summary>Only known Card Print settings are reset. No vault, key file, remote archive,
/// updater binary, Proton CLI, or Windows Credential Manager entry is deleted.</summary>
public static class SettingsResetService
{
    private static readonly string[] ConfigFiles =
    [
        "ui-shell.json", "vault-state.json", "appearance.json", "mapping.json",
        "domains.json", "appearance-template-source.json"
    ];

    internal static string BackupBase => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "keepassxccardprint-reset-backups");
    private static string OutcomePath => Path.Combine(BackupBase, "last-reset-result.txt");

    /// <summary>Called only from the worker after the original process has exited.</summary>
    public static SettingsResetResult Execute(SettingsResetOptions options)
    {
        var appRoot = SettingsLocation.AppDataRoot;
        var roots = options.AllStorages
            ? new[] { appRoot, SettingsLocation.LocalRoot, SettingsLocation.CloudRoot }
            : SettingsLocation.Mode == ConfigurationStorage.Cloud
                // After resetting a cloud cache we return to AppData. Do not revive
                // the previous AppData settings on the first post-reset launch.
                ? new[] { SettingsLocation.CloudRoot, appRoot }
                : new[] { SettingsLocation.DirectoryPath };
        var resetCloud = options.AllStorages || SettingsLocation.Mode == ConfigurationStorage.Cloud;
        // A reset of Cloud/Local that returns to AppData must not make the active
        // print ledger disappear from the UI when the user chose to keep history.
        // Existing AppData ledgers are never overwritten: both copies remain safe.
        if (resetCloud && !options.ClearPrintHistory &&
            SettingsLocation.Mode != ConfigurationStorage.AppData)
            PreserveHistoryForSystem(SettingsLocation.DirectoryPath, appRoot);
        var extra = new List<string>();
        if (resetCloud)
        {
            extra.Add(Path.Combine(appRoot, "cloud-verified.json"));
            extra.Add(Path.Combine(appRoot, "cloud-sync-journal.json"));
            extra.Add(Path.Combine(appRoot, "cloud-mapping-review-pending.flag"));
            extra.Add(Path.Combine(appRoot, "storage-mode.txt"));
        }
        if (options.AllStorages)
        {
            extra.Add(Path.Combine(appRoot, "cloud-connection.json"));
            extra.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KeePassXCCardPrint", "updates", "preferences.txt"));
        }
        var result = ExecuteForRoots(roots, BackupBase, options.ClearPrintHistory, extra);
        // With a remaining portable data/ directory, deleting storage-mode.txt
        // without replacing it would reactivate the previous local configuration.
        if (resetCloud) SettingsLocation.Activate(ConfigurationStorage.AppData);
        return result;
    }

    // Copy only absent ledgers. On a name collision retain both originals,
    // because merging potentially conflicting per-card print states is unsafe.
    internal static int PreserveHistoryForSystem(string activeRoot, string systemRoot)
    {
        if (Path.GetFullPath(activeRoot).Equals(Path.GetFullPath(systemRoot),
                StringComparison.OrdinalIgnoreCase)) return 0;
        var source = Path.Combine(activeRoot, "print-tracking");
        if (!Directory.Exists(source)) return 0;
        RejectLink(source);
        var destination = Path.Combine(systemRoot, "print-tracking");
        Directory.CreateDirectory(destination);
        RejectLink(destination);
        var copied = 0;
        foreach (var entry in Directory.EnumerateFiles(source, "*.json", SearchOption.TopDirectoryOnly))
        {
            RejectLink(entry);
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if (File.Exists(target))
            {
                RejectLink(target);
                continue; // Preserve both; never overwrite a separate ledger.
            }
            var stage = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(entry, stage, false);
                if (!SameContent(entry, stage))
                    throw new IOException("Print history copy failed verification; settings were not reset.");
                File.Move(stage, target, false);
                copied++;
            }
            finally { if (File.Exists(stage)) File.Delete(stage); }
        }
        return copied;
    }

    /// <summary>Use a separate backup root; do not delete any data until all
    /// backups have been verified. Roll back already deleted files on failure.</summary>
    internal static SettingsResetResult ExecuteForRoots(IEnumerable<string> roots,
        string backupBase, bool clearPrintHistory, IEnumerable<string>? additionalFiles = null)
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<(string Path, string Relative)>();
        var rootIndex = 0;
        foreach (var root in roots.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var label = "storage-" + rootIndex++;
            if (!Directory.Exists(root)) continue;
            RejectLink(root);
            foreach (var name in ConfigFiles) Add(Path.Combine(root, name), label + "/" + name);
            CollectJsonFolder(root, "templates", label);
            if (clearPrintHistory) CollectJsonFolder(root, "print-tracking", label);
        }
        var extraIndex = 0;
        if (additionalFiles is not null)
            foreach (var file in additionalFiles)
                Add(file, "machine/" + extraIndex++ + "-" + Path.GetFileName(file));

        void CollectJsonFolder(string root, string folderName, string label)
        {
            var folder = Path.Combine(root, folderName);
            if (!Directory.Exists(folder)) return;
            RejectLink(folder);
            foreach (var file in Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
                Add(file, label + "/" + folderName + "/" + Path.GetFileName(file));
        }
        void Add(string path, string relative)
        {
            path = Path.GetFullPath(path);
            if (!File.Exists(path) || !unique.Add(path)) return;
            RejectLink(path);
            ordered.Add((path, relative));
        }

        var backupRoot = Path.Combine(Path.GetFullPath(backupBase),
            "reset-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupRoot);
        foreach (var (source, relative) in ordered)
        {
            var destination = Path.Combine(backupRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, false);
            if (!SameContent(source, destination))
                throw new IOException("Configuration backup checksum verification failed. Nothing was reset.");
        }
        File.WriteAllText(Path.Combine(backupRoot, "RESET-INFO.txt"),
            "KeePassXC Card Print settings reset (local files only)." + Environment.NewLine +
            "Print history cleared: " + clearPrintHistory + Environment.NewLine +
            "Saved files: " + ordered.Count + Environment.NewLine +
            "Remote configuration.zip and Windows credentials were not modified." + Environment.NewLine,
            Encoding.UTF8);
        var deleted = new List<(string Source, string Relative)>();
        try
        {
            foreach (var (source, relative) in ordered)
            {
                RejectLink(source);
                File.Delete(source);
                deleted.Add((source, relative));
            }
        }
        catch
        {
            // Preserve backup even if restoring the originals also fails.
            foreach (var (source, relative) in deleted)
            {
                var saved = Path.Combine(backupRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                File.Copy(saved, source, true);
            }
            throw;
        }
        return new SettingsResetResult(backupRoot, ordered.Count);
    }

    internal static void StoreOutcome(bool success, string message)
    {
        Directory.CreateDirectory(BackupBase);
        var temp = OutcomePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, (success ? "OK" : "ERROR") + "\n" + message, Encoding.UTF8);
            File.Move(temp, OutcomePath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static SettingsResetOutcome? ConsumeOutcome()
    {
        try
        {
            if (!File.Exists(OutcomePath) || new FileInfo(OutcomePath).Length > 4096) return null;
            var lines = File.ReadAllLines(OutcomePath);
            File.Delete(OutcomePath);
            if (lines.Length != 2 || lines[0] is not ("OK" or "ERROR")) return null;
            return new SettingsResetOutcome(lines[0] == "OK", lines[1]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A linked configuration path cannot be reset safely.");
    }
    private static bool SameContent(string first, string second)
    {
        using var a = File.OpenRead(first);
        using var b = File.OpenRead(second);
        return a.Length == b.Length &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(a), SHA256.HashData(b));
    }
}
