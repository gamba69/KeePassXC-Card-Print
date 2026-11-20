namespace KeePassXCCardPrint.Services;

/// <summary>Non-destructive reset regression checks, invoked by --self-test-cloud.</summary>
internal static class SettingsResetSelfTests
{
    internal static void Run()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "kpc-reset-selftest-" + Guid.NewGuid().ToString("N"));
        var system = Path.Combine(workspace, "system");
        var local = Path.Combine(workspace, "local");
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
            Assert(File.Exists(Path.Combine(local, "do-not-touch.txt")),
                "existing test files are initially present");
            Directory.CreateDirectory(Path.Combine(local, "print-tracking"));
            File.WriteAllText(Path.Combine(local, "print-tracking", "status.json"), "status");
            Directory.CreateDirectory(Path.Combine(local, "obsolete"));
            File.WriteAllText(Path.Combine(local, "obsolete", "stale.tmp"), "old");
            ConfigurationMaintenance.CleanRoot(local, fullReset: false, clearTracking: false);
            Assert(!Directory.Exists(Path.Combine(local, "obsolete")) &&
                !File.Exists(Path.Combine(local, "do-not-touch.txt")) &&
                File.Exists(Path.Combine(local, "print-tracking", "status.json")),
                "startup removes unknown files without changing print-tracking");
            ConfigurationMaintenance.CleanRoot(local, fullReset: true, clearTracking: false);
            Assert(Directory.Exists(Path.Combine(local, "print-tracking")),
                "reset without explicit tracking deletion preserves the entire directory");
            ConfigurationMaintenance.CleanRoot(local, fullReset: true, clearTracking: true);
            Assert(!Directory.Exists(Path.Combine(local, "print-tracking")),
                "explicit tracking deletion removes the entire directory");
            var retention = Path.Combine(workspace, "retention");
            Directory.CreateDirectory(retention);
            for (var i = 0; i < 5; i++)
            {
                var dir = Path.Combine(retention, $"2026-11-20_12-00-0{i}");
                Directory.CreateDirectory(dir);
                foreach (var name in new[] { "system", "local", "cloud-cache" })
                    Directory.CreateDirectory(Path.Combine(dir, name));
                File.WriteAllText(Path.Combine(dir, "system", "settings.json"), "good-" + i);
                ConfigurationMaintenance.SealBackup(dir);
            }
            var partial = Path.Combine(retention, "old-storage-switch-partial");
            Directory.CreateDirectory(partial);
            File.WriteAllText(Path.Combine(partial, "mapping.json"), "partial");
            ConfigurationMaintenance.PruneBackups(retention);
            Assert(Directory.EnumerateDirectories(retention).Count() == 3 &&
                !Directory.Exists(partial) &&
                Directory.Exists(Path.Combine(retention, "2026-11-20_12-00-04")),
                "only three latest verified full backups remain; partial backups are removed");
            var damaged = Path.Combine(retention, "2026-11-20_12-00-04", "system", "settings.json");
            File.WriteAllText(damaged, "corrupted");
            ConfigurationMaintenance.PruneBackups(retention);
            Assert(!Directory.Exists(Path.GetDirectoryName(Path.GetDirectoryName(damaged)!)),
                "backups with broken checksums are not retained");
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
