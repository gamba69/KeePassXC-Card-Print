using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace KeePassXCCardPrint.Services;

// Executed inside the published Native AOT EXE: KeePassXCCardPrint.exe --self-test-cloud
public static class CloudArchiveSelfTests
{
    public static string Report { get; private set; } = "";
    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("[FAIL] " + name);
    }
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("[FAIL] " + name);
    }
    public static int Run()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "kpc-cloud-test-" + Guid.NewGuid().ToString("N"));
        var log = new StringBuilder();
        try
        {
            var source = Path.Combine(scratch, "source");
            var destination = Path.Combine(scratch, "destination");
            var stage = Path.Combine(scratch, "stage");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(destination);
            var sourceUi = new UiSettings { Language = "uk", Theme = "dark",
                WindowWidth = 1000, LastPrintDevice = "source-printer" };
            new UiSettingsStore(source).Save(sourceUi);
            var destinationUi = new UiSettings { Language = "en", Theme = "light",
                WindowWidth = 1580, LastPrintDevice = "machine-printer",
                PrintCalibrations = new() { ["local-printer"] = new PrintCalibration { FrontX = 1.5 } } };
            new UiSettingsStore(destination).Save(destinationUi);
            File.WriteAllText(Path.Combine(source, "mapping.json"), "{\"email\":{\"role\":\"text\"}}");
            File.WriteAllText(Path.Combine(source, "appearance.json"), "{\"version\":1}");
            Directory.CreateDirectory(Path.Combine(source, "templates"));
            File.WriteAllText(Path.Combine(source, "templates", "example.json"), "{\"format\":\"KeePassCardPrint-template-v1\",\"name\":\"test\",\"settings\":{}}");
            Directory.CreateDirectory(Path.Combine(source, "print-tracking"));
            var tracking = new string('a', 64) + ".json";
            File.WriteAllText(Path.Combine(source, "print-tracking", tracking),
                "{\"Version\":1,\"History\":[{\"Result\":\"printed\"}]}");
            var archive = Path.Combine(scratch, "config.zip");
            var manifest = CloudConfigurationArchive.Create(source, archive,
                Guid.NewGuid(), DateTime.UtcNow);
            Assert(manifest.Files.Count == 5, "portable files enumerated");
            log.AppendLine("[PASS] Allow-list and print-tracking history included");
            var verified = CloudConfigurationArchive.ReadVerified(archive, stage);
            CloudConfigurationArchive.ApplyVerified(destination, stage, verified,
                Path.Combine(scratch, "backups"));
            var applied = new UiSettingsStore(destination).Load();
            Assert(applied.Language == "uk" && applied.Theme == "dark" &&
                applied.WindowWidth == 1580 && applied.LastPrintDevice == "machine-printer" &&
                applied.PrintCalibrations["local-printer"].FrontX == 1.5,
                "machine preferences must survive remote apply");
            Assert(File.ReadAllText(Path.Combine(destination, "print-tracking", tracking)) ==
                File.ReadAllText(Path.Combine(source, "print-tracking", tracking)),
                "complete print history");
            log.AppendLine("[PASS] Portable apply preserves machine settings, printing history");
            var customFolder = new CloudConnectionProfile { RemoteFolder = "Backups/KeePassXC-Card-Print" };
            Assert(CloudConnectionStore.RemoteFilePath(customFolder) ==
                "Backups/KeePassXC-Card-Print/configuration.zip", "custom remote folder");
            var unsafeFolder = new CloudConnectionProfile { RemoteFolder = "../escape" };
            try
            {
                CloudConnectionStore.RemoteFolder(unsafeFolder);
                throw new InvalidOperationException("[FAIL] Unsafe remote folder accepted");
            }
            catch (ArgumentException) { /* Expected. */ }
            log.AppendLine("[PASS] Custom folder and remote path traversal rejection");
            var tie = new CloudSyncJournal { SavedAtUtc = manifest.SavedAtUtc };
            Assert(CloudConfigurationSync.RemoteWins(manifest, tie), "tie must favor cloud");
            Assert(!CloudConfigurationSync.RemoteWins(manifest, tie, forceUpload: true),
                "explicit upload must win");
            tie.SavedAtUtc = manifest.SavedAtUtc.AddSeconds(10);
            Assert(!CloudConfigurationSync.RemoteWins(manifest, tie), "newer local must win");
            Assert(CloudConfigurationSync.RemoteWins(manifest, tie, forceDownload: true),
                "explicit download must win");
            log.AppendLine("[PASS] Last Write Wins and equal timestamp policy");
            var altered = Path.Combine(scratch, "tampered.zip");
            using (var output = ZipFile.Open(altered, ZipArchiveMode.Create))
            using (var input = ZipFile.OpenRead(archive))
            {
                foreach (var entry in input.Entries)
                {
                    using var destinationStream = output.CreateEntry(entry.FullName).Open();
                    if (entry.FullName == "mapping.json")
                    {
                        var bytes = Encoding.UTF8.GetBytes("{\"email\":{\"role\":\"evil\"}}");
                        destinationStream.Write(bytes);
                    }
                    else using (var stream = entry.Open()) stream.CopyTo(destinationStream);
                }
            }
            Reject(() => CloudConfigurationArchive.ReadVerified(altered,
                Path.Combine(scratch, "tampered-stage")), "tampered JSON / SHA-256");
            log.AppendLine("[PASS] SHA-256 mismatch rejected");
            var traversal = Path.Combine(scratch, "traversal.zip");
            using (var output = ZipFile.Open(traversal, ZipArchiveMode.Create))
            {
                using var e = output.CreateEntry("../escape.json").Open();
                e.Write(Encoding.UTF8.GetBytes("{}"));
            }
            Reject(() => CloudConfigurationArchive.ReadVerified(traversal,
                Path.Combine(scratch, "traversal-stage")), "ZIP traversal rejected");
            log.AppendLine("[PASS] ZIP traversal rejected");
            var unknown = Path.Combine(scratch, "private.zip");
            using (var output = ZipFile.Open(unknown, ZipArchiveMode.Create))
            {
                using var e = output.CreateEntry("vault-state.json").Open();
                e.Write(Encoding.UTF8.GetBytes("{}"));
            }
            Reject(() => CloudConfigurationArchive.ReadVerified(unknown,
                Path.Combine(scratch, "private-stage")), "secrets disallowed");
            log.AppendLine("[PASS] Vault state / secrets rejected");
            Report = log.ToString();
            return 0;
        }
        catch (Exception ex)
        {
            Report = log + "[FAIL] " + ex.GetType().Name + ": " + ex.Message;
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch (IOException) { }
        }
    }
}
