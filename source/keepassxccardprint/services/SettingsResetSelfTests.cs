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
            // Simulate a second launch after the user saved a camera. Use the
            // actual DPAPI/JSON store on Windows, never the production AppData.
            var scannerFiles = new[] { "scanner-network.dpapi", "scanner-network-selection.txt", "scanner-decoders.json" };
            var camera = new NetworkCameraProfile
            {
                Name = "Synthetic camera", Address = "rtsp://127.0.0.1:8554/demo",
                User = "synthetic", Password = Guid.NewGuid().ToString("N"), AllowPlaintext = true,
                Engine = "sharp", Decoder = "libde265", Fps = 7, TimeoutSeconds = 15, Reconnect = false
            };
            var cameraFile = Path.Combine(system, scannerFiles[0]);
            if (OperatingSystem.IsWindows()) NetworkCameraProfiles.SaveTo(cameraFile, [camera]);
            else File.WriteAllBytes(cameraFile, [1, 2, 3]);
            File.WriteAllText(Path.Combine(system, scannerFiles[1]), camera.Id);
            File.WriteAllText(Path.Combine(system, scannerFiles[2]), "{}");
            var ffmpeg = Path.Combine(system, "components", "ffmpeg");
            Directory.CreateDirectory(ffmpeg);
            File.WriteAllText(Path.Combine(ffmpeg, "test-marker.txt"), "installed");
            File.WriteAllText(Path.Combine(system, "scanner-network.dpapi.stale.tmp"), "interrupted write");
            var originals = scannerFiles.Select(name => File.ReadAllBytes(Path.Combine(system, name))).ToArray();
            for (int launch = 0; launch < 2; launch++)
            {
                ConfigurationMaintenance.CleanRoot(system, fullReset: false, clearTracking: false, system: true);
                Assert(scannerFiles.Select((name, i) => File.ReadAllBytes(Path.Combine(system, name)).SequenceEqual(originals[i])).All(ok => ok),
                    "startup preserves device-local scanner files byte for byte");
                Assert(File.Exists(Path.Combine(ffmpeg, "test-marker.txt")) &&
                    !File.Exists(Path.Combine(system, "scanner-network.dpapi.stale.tmp")),
                    "startup preserves installed components but removes stale temporary files");
                if (OperatingSystem.IsWindows())
                {
                    var saved = NetworkCameraProfiles.LoadFrom(cameraFile).Single();
                    Assert(saved.Id == camera.Id && saved.Name == camera.Name && saved.Address == camera.Address &&
                        saved.User == camera.User && saved.Password == camera.Password && saved.AllowPlaintext &&
                        saved.Engine == camera.Engine && saved.Decoder == camera.Decoder && saved.Fps == camera.Fps &&
                        saved.TimeoutSeconds == camera.TimeoutSeconds && saved.Reconnect == camera.Reconnect,
                        "saved camera survives cleanup with credentials and settings intact");
                }
            }
            // The scanner files must not become portable/cloud configuration.
            foreach (var name in scannerFiles) File.WriteAllText(Path.Combine(local, name), "misplaced");
            ConfigurationMaintenance.CleanRoot(local, fullReset: false, clearTracking: false);
            Assert(scannerFiles.All(name => !File.Exists(Path.Combine(local, name))),
                "non-system roots do not retain device-local scanner files");
            ConfigurationMaintenance.CleanRoot(system, fullReset: true, clearTracking: false, system: true);
            Assert(scannerFiles.All(name => !File.Exists(Path.Combine(system, name))) && !Directory.Exists(Path.Combine(system, "components")),
                "explicit full reset still removes scanner settings and optional components");
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
