namespace KeePassXCCardPrint.Services;

/// <summary>Non-destructive reset regression checks, invoked by --self-test-cloud.</summary>
internal static class SettingsResetSelfTests
{
    internal static void Run()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "kpc-reset-selftest-" + Guid.NewGuid().ToString("N"));
        var system = Path.Combine(workspace, "system");
        var local = Path.Combine(workspace, "local");
        var backups = Path.Combine(workspace, "backups");
        Directory.CreateDirectory(system);
        Directory.CreateDirectory(local);
        try
        {
            foreach (var root in new[] { system, local })
            {
                Directory.CreateDirectory(Path.Combine(root, "templates"));
                Directory.CreateDirectory(Path.Combine(root, "print-tracking"));
                File.WriteAllText(Path.Combine(root, "mapping.json"), "{\"email\":{\"Role\":\"text\"}}");
                File.WriteAllText(Path.Combine(root, "templates", "a.json"), "{}");
                File.WriteAllText(Path.Combine(root, "print-tracking", "one.json"), "{\"History\":[]}");
                File.WriteAllText(Path.Combine(root, "do-not-touch.txt"), "unchanged");
            }
            var fallback = Path.Combine(workspace, "fallback");
            var moved = SettingsResetService.PreserveHistoryForSystem(local, fallback);
            Assert(moved == 1 && File.Exists(Path.Combine(fallback, "print-tracking", "one.json")),
                "print tracking stays visible after a storage-mode fallback");
            // Existing files must not be silently overwritten on a second transfer.
            File.WriteAllText(Path.Combine(fallback, "print-tracking", "one.json"), "previous history");
            Assert(SettingsResetService.PreserveHistoryForSystem(local, fallback) == 0 &&
                File.ReadAllText(Path.Combine(fallback, "print-tracking", "one.json")) == "previous history",
                "conflicting print tracking data remains intact");
            var first = SettingsResetService.ExecuteForRoots([system], backups, false);
            Assert(first.FileCount == 2 && !File.Exists(Path.Combine(system, "mapping.json")),
                "current settings deleted");
            Assert(File.Exists(Path.Combine(system, "print-tracking", "one.json")) &&
                File.ReadAllText(Path.Combine(system, "do-not-touch.txt")) == "unchanged" &&
                File.Exists(Path.Combine(local, "mapping.json")), "history and other storages preserved");
            Assert(File.Exists(Path.Combine(first.BackupDirectory, "storage-0", "mapping.json")),
                "verified settings backup exists");
            var second = SettingsResetService.ExecuteForRoots([system, local], backups, true);
            Assert(!File.Exists(Path.Combine(system, "print-tracking", "one.json")) &&
                !File.Exists(Path.Combine(local, "print-tracking", "one.json")) &&
                !File.Exists(Path.Combine(local, "mapping.json")), "explicit history clear applies to all roots");
            Assert(File.Exists(Path.Combine(second.BackupDirectory, "storage-1", "print-tracking", "one.json")),
                "print history backed up before deletion");
            Assert(File.Exists(Path.Combine(local, "do-not-touch.txt")), "unrelated user file untouched");
        }
        finally
        {
            try { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }
            catch (IOException) { }
        }
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("[FAIL] settings reset: " + message);
    }
}
