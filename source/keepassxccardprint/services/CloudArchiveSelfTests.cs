using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using KeePassXC.CloudTransport;

namespace KeePassXCCardPrint.Services;

// Executed inside the published Native AOT EXE: KeePassXCCardPrint.exe --self-test-cloud
public static class CloudArchiveSelfTests
{
    // The explicit probe must not inspect or touch the production ZIP. Its
    // isolated network substitute stores only the random verification object.
    private sealed class CloudProbeStore : ICloudTransport
    {
        public readonly Dictionary<string, byte[]> Objects = new(StringComparer.Ordinal);
        public readonly List<string> Operations = [];
        public CloudProvider Provider => CloudProvider.WebDav;
        public CloudCapabilities Capabilities => new(false, false, false);
        public Task<bool> CheckConnectionAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> DownloadAsync(string path, string destination, CancellationToken ct)
        {
            Operations.Add("download:" + path);
            if (!Objects.TryGetValue(path, out var bytes)) return Task.FromResult(false);
            File.WriteAllBytes(destination, bytes);
            return Task.FromResult(true);
        }
        public Task UploadAsync(string source, string path, CloudWriteMode mode, CancellationToken ct)
        {
            Operations.Add("upload:" + path);
            if (mode == CloudWriteMode.CreateNew && Objects.ContainsKey(path))
                throw new IOException("Cloud test object exists");
            Objects[path] = File.ReadAllBytes(source);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<CloudObjectInfo>> ListAsync(string directory, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<CloudObjectInfo>>([]);
        public Task<CloudObjectInfo?> GetInfoAsync(string path, CancellationToken ct)
            => Task.FromResult<CloudObjectInfo?>(null);
        public Task EnsureDirectoryAsync(string directory, CancellationToken ct)
        { Operations.Add("directory:" + directory); return Task.CompletedTask; }
        public Task DeleteAsync(string path, CancellationToken ct)
        { Operations.Add("delete:" + path); Objects.Remove(path); return Task.CompletedTask; }
        public void Dispose() { }
    }
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
            SettingsResetSelfTests.Run();
            log.AppendLine("[PASS] Settings reset preserves print tracking unless explicitly selected");
            var delivered = 0;
            var reported = 0;
            Action<string, bool> listeners = (_, _) => throw new InvalidOperationException("test observer");
            listeners += (_, _) => delivered++;
            StorageSwitchSafety.Notify(listeners, "test", true, _ => reported++);
            Assert(delivered == 1 && reported == 1,
                "a throwing observer cannot suppress later observers or crash storage selection");
            StorageSwitchSafety.BestEffort(() => throw new NullReferenceException("test UI"),
                _ => reported++);
            Assert(reported == 2, "UI recovery operations swallow and report their failures");
            log.AppendLine("[PASS] Storage operation observer isolation and guarded UI recovery");
            var sourceUi = new UiSettings { Language = "uk", Theme = "dark",
                WindowWidth = 1000, LastPrintDevice = "source-printer" };
            new UiSettingsStore(source).Save(sourceUi);
            var destinationUi = new UiSettings { Language = "en", Theme = "light",
                WindowWidth = 1580, LastPrintDevice = "machine-printer",
                PrintCalibrations = new() { ["local-printer"] = new PrintCalibration { FrontX = 1.5 } } };
            new UiSettingsStore(destination).Save(destinationUi);
            File.WriteAllText(Path.Combine(source, "appearance.json"), "{\"version\":1}");
            Directory.CreateDirectory(Path.Combine(source, "templates"));
            var embedded = "{\"format\":\"KeePassCardPrint-template-v1\",\"name\":\"test\",\"settings\":{},\"mapping\":{\"email\":{\"Role\":\"text\"}}}";
            var templatePath = Path.Combine(source, "templates", "example.json");
            File.WriteAllText(templatePath, embedded);
            Directory.CreateDirectory(Path.Combine(source, "print-tracking"));
            var tracking = new string('a', 64) + ".json";
            File.WriteAllText(Path.Combine(source, "print-tracking", tracking),
                "{\"Version\":1,\"History\":[{\"Result\":\"printed\"}]}");
            CloudConfigurationArchive.ValidateLocal(source);
            using (var parsed = System.Text.Json.JsonDocument.Parse(embedded))
                Assert(CardAppearanceStore.ParseMapping(parsed.RootElement).ContainsKey("email"),
                    "embedded field mapping is parsed from the template");
            File.WriteAllText(templatePath,
                "{\"format\":\"KeePassCardPrint-template-v1\",\"name\":\"test\",\"settings\":{},\"mapping\":[]}");
            Reject(() => CloudConfigurationArchive.ValidateLocal(source),
                "invalid embedded field mapping rejected before upload");
            File.WriteAllText(templatePath, embedded);
            log.AppendLine("[PASS] Embedded mapping validated with template before cloud upload");
            var archive = Path.Combine(scratch, "config.zip");
            var manifest = CloudConfigurationArchive.Create(source, archive,
                Guid.NewGuid(), DateTime.UtcNow);
            Assert(manifest.Files.Count == 4 &&
                !manifest.Files.Any(x => x.Path == "mapping.json"),
                "portable archive contains full templates, not standalone mapping");
            log.AppendLine("[PASS] Template mapping and print-tracking included");
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
            var oldR2 = new CloudConnectionProfile
            {
                Provider = "CloudflareR2", Endpoint = "https://account.r2.cloudflarestorage.com",
                Bucket = "my-bucket", Region = "auto", CheckProtonCliAutomatically = true
            };
            var noCheck = new CloudConnectionProfile
            {
                Provider = oldR2.Provider, Endpoint = oldR2.Endpoint,
                Bucket = oldR2.Bucket, Region = oldR2.Region,
                CheckProtonCliAutomatically = false
            };
            Assert(CloudConnectionStore.UiProvider(oldR2.Provider) == "S3" &&
                CloudConnectionStore.ProviderForSelection("S3", oldR2, oldR2.Endpoint) == "CloudflareR2" &&
                CloudConnectionStore.ProviderForSelection("S3", oldR2, "https://s3.example.invalid") == "S3",
                "legacy R2 uses the unified S3 selector without changing its destination");
            Assert(CloudConnectionStore.SameConnection(oldR2, noCheck) &&
                !CloudConnectionStore.SameProfile(oldR2, noCheck),
                "Proton CLI update preference is not a cloud connection change");
            var recent = DateTime.UtcNow;
            var verifiedRecord = new CloudConnectionVerification
            {
                ProfileHash = CloudConnectionStore.ConnectionFingerprint(oldR2), VerifiedUtc = recent
            };
            Assert(CloudConnectionStore.VerificationMatches(verifiedRecord, noCheck),
                "verification survives preference changes and application restart");
            Assert(!CloudConnectionStore.VerificationMatches(
                    new CloudConnectionVerification { ProfileHash = "old-format", VerifiedUtc = recent }, oldR2),
                "obsolete verification records are not accepted");
            var otherBucket = new CloudConnectionProfile
            {
                Provider = oldR2.Provider, Endpoint = oldR2.Endpoint,
                Bucket = "other-bucket", Region = oldR2.Region
            };
            Assert(!CloudConnectionStore.VerificationMatches(verifiedRecord, otherBucket),
                "a different S3 bucket cannot reuse a verified connection");
            log.AppendLine("[PASS] Unified S3/R2 selection and current verification fingerprint");
            var configured = new CloudConnectionProfile
            {
                Provider = "GoogleDrive", RemoteFolder = "KeePassXC-Card-Print"
            };
            var bound = new CloudSyncJournal
            {
                SnapshotId = manifest.SnapshotId,
                RemoteTargetHash = CloudConnectionStore.TargetFingerprint(configured)
            };
            Assert(CloudConfigurationSync.DestinationMatches(bound, configured, manifest),
                "retesting the same verified destination keeps the existing binding");
            var displayOnly = new CloudConnectionProfile
            {
                Provider = "GoogleDrive", RemoteFolder = configured.RemoteFolder,
                CheckProtonCliAutomatically = false, ProtonExecutable = "different-cli.exe"
            };
            Assert(CloudConfigurationSync.DestinationMatches(bound, displayOnly, manifest),
                "CLI preferences cannot change the remote destination");
            var moved = new CloudConnectionProfile
            {
                Provider = "GoogleDrive", RemoteFolder = "Backups/New-Destination"
            };
            Assert(!CloudConfigurationSync.DestinationMatches(bound, moved, manifest),
                "a changed remote folder requires explicit confirmation");
            var unbound = new CloudSyncJournal { SnapshotId = manifest.SnapshotId };
            Assert(!CloudConfigurationSync.DestinationMatches(unbound, configured, manifest),
                "unbound journals cannot claim an existing cloud destination");
            log.AppendLine("[PASS] Connection retest has no implicit transfer; strict destination binding");
            var cloudProbe = new CloudProbeStore();
            var originalArchive = Encoding.UTF8.GetBytes("must-not-be-modified");
            var archiveKey = CloudConnectionStore.RemoteFilePath(configured);
            cloudProbe.Objects[archiveKey] = originalArchive.ToArray();
            CloudTransportAdapter.VerifyWithTransportAsync(configured, cloudProbe,
                CancellationToken.None).GetAwaiter().GetResult();
            Assert(cloudProbe.Objects.Count == 1 &&
                cloudProbe.Objects[archiveKey].AsSpan().SequenceEqual(originalArchive) &&
                cloudProbe.Operations.Any(x => x.StartsWith("upload:", StringComparison.Ordinal)) &&
                cloudProbe.Operations.Any(x => x.StartsWith("download:", StringComparison.Ordinal)) &&
                cloudProbe.Operations.Any(x => x.StartsWith("delete:", StringComparison.Ordinal)) &&
                !cloudProbe.Operations.Any(x => x.Contains("configuration.zip", StringComparison.Ordinal)),
                "connection probe leaves the real archive completely untouched");
            log.AppendLine("[PASS] Real-archive isolation during connection probe");
            foreach (var language in new[] { "ru", "uk", "en" })
            foreach (var darkTheme in new[] { true, false })
            {
                var pages = CloudOAuthCallbackPages.Create(language, darkTheme);
                Assert(pages.SuccessHtml.Contains("KeePassXC Card Print", StringComparison.Ordinal) &&
                    pages.SuccessHtml.Contains("Google Drive", StringComparison.Ordinal) &&
                    pages.FailureHtml.Contains("Google Drive", StringComparison.Ordinal) &&
                    !pages.SuccessHtml.Contains("<script", StringComparison.OrdinalIgnoreCase) &&
                    !pages.FailureHtml.Contains("<script", StringComparison.OrdinalIgnoreCase) &&
                    Encoding.UTF8.GetByteCount(pages.SuccessHtml) <= 128 * 1024 &&
                    Encoding.UTF8.GetByteCount(pages.FailureHtml) <= 128 * 1024,
                    "OAuth result HTML is localized, standalone and bounded");
            }
            log.AppendLine("[PASS] OAuth result pages RU/UK/EN, dark/light, no scripts");
            var id = Guid.NewGuid();
            var live = new CloudTransferProgress(id, CloudProvider.WebDav,
                CloudTransferDirection.Upload, CloudTransferPhase.Transferring, 2048, 4096, true);
            Assert(CloudTransferPresentation.Matches(live, "sync.upload", "Uploading", true) &&
                !CloudTransferPresentation.Matches(live, "sync.remote", "Uploading", true) &&
                CloudTransferPresentation.Percent(live) == 50d,
                "display only matching real byte progress");
            var proton = live with { Provider = CloudProvider.ProtonDrive, HasLiveByteProgress = false };
            Assert(CloudTransferPresentation.Percent(proton) is null &&
                !CloudTransferPresentation.Describe(proton, "en").Contains("%", StringComparison.Ordinal),
                "never display fake Proton CLI percent");
            var unknownLength = live with { TotalBytes = null };
            Assert(CloudTransferPresentation.Percent(unknownLength) is null,
                "unknown content length is indeterminate");
            log.AppendLine("[PASS] Transport bytes, phase filtering and Proton progress presentation");
            var uploadDecision = KeePassXCCardPrint.StorageSwitchDialog.Describe("ru",
                ConfigurationStorage.AppData, ConfigurationStorage.Cloud,
                "machine-config", "local-cloud-cache", "Google Drive / KeePassXC-Card-Print/configuration.zip",
                "local-cloud-cache");
            Assert(uploadDecision.FirstAction == "Отправить в облако" &&
                uploadDecision.FirstRoute.Contains("machine-config", StringComparison.Ordinal) &&
                uploadDecision.FirstRoute.Contains("configuration.zip", StringComparison.Ordinal) &&
                uploadDecision.SecondAction == "Загрузить из облака" &&
                uploadDecision.SecondRoute.Contains("local-cloud-cache", StringComparison.Ordinal),
                "cloud switch offers explicit upload/download directions");
            var localDecision = KeePassXCCardPrint.StorageSwitchDialog.Describe("uk",
                ConfigurationStorage.Cloud, ConfigurationStorage.Local,
                "cloud-cache", "local-folder", "", "cloud-cache");
            Assert(localDecision.FirstRoute.Contains("cloud-cache", StringComparison.Ordinal) &&
                localDecision.FirstRoute.Contains("local-folder", StringComparison.Ordinal) &&
                localDecision.SecondRoute.Contains("local-folder", StringComparison.Ordinal) &&
                !localDecision.SecondRoute.Contains("cloud-cache", StringComparison.Ordinal),
                "local switch uses local cache, not a live remote download");
            var newDestination = KeePassXCCardPrint.StorageSwitchDialog.Describe("en",
                ConfigurationStorage.Cloud, ConfigurationStorage.Cloud,
                "old-cache", "old-cache", "S3 / backups/configuration.zip", "old-cache",
                changingCloudDestination: true);
            Assert(newDestination.Intro.Contains("new cloud folder", StringComparison.Ordinal) &&
                newDestination.FirstRoute.Contains("S3 / backups/configuration.zip", StringComparison.Ordinal) &&
                newDestination.SecondRoute.Contains("old-cache", StringComparison.Ordinal),
                "new cloud destination has explicit transfer directions");
            log.AppendLine("[PASS] Storage switch choices show source, destination and safe direction");
            var cloudSignature = CloudSavePolicy.ManifestSignature(manifest);
            var journal = new CloudSyncJournal { SnapshotId = manifest.SnapshotId, SavedAtUtc = manifest.SavedAtUtc,
                LocalSignature = cloudSignature, LastSuccessfulUtc = DateTime.UtcNow };
            CloudSavePolicy.Migrate(journal);
            Assert(journal.ConfirmedSnapshotId == manifest.SnapshotId && journal.ConfirmedSignature == cloudSignature,
                "migrate confirmed legacy journal");
            Assert(!CloudSavePolicy.NeedsUpload(journal, cloudSignature), "clean local cache must not upload on exit");
            Assert(CloudSavePolicy.Decide(journal, cloudSignature, manifest) == CloudSaveDecision.Unchanged,
                "unchanged check is a no-op");
            Assert(CloudSavePolicy.Decide(journal, "new-edit", manifest) == CloudSaveDecision.Upload,
                "local changes with unchanged remote can upload");
            var other = new CloudArchiveManifest { SnapshotId = Guid.NewGuid(), SavedAtUtc = manifest.SavedAtUtc.AddSeconds(-10) };
            Assert(CloudSavePolicy.Decide(journal, cloudSignature, other) == CloudSaveDecision.Download,
                "changed remote is applied when local is clean, independent of clock skew");
            Assert(CloudSavePolicy.Decide(journal, "new-edit", other) == CloudSaveDecision.Conflict,
                "simultaneous changes never silently overwrite either copy");
            Assert(CloudSavePolicy.Decide(journal, "new-edit", other, forceUpload: true) == CloudSaveDecision.Upload &&
                CloudSavePolicy.Decide(journal, "new-edit", other, forceDownload: true) == CloudSaveDecision.Download,
                "only explicit resolution can select a conflicting copy");
            CloudSavePolicy.Confirm(journal, manifest, cloudSignature, "new-edit");
            Assert(journal.Pending && CloudSavePolicy.NeedsUpload(journal, "new-edit"),
                "confirmed older upload must not clear newer edits");
            CloudSavePolicy.Confirm(journal, manifest, cloudSignature, cloudSignature);
            Assert(!journal.Pending && !CloudSavePolicy.NeedsUpload(journal, cloudSignature), "confirmed latest save releases exit barrier");
            journal.Pending = true;
            Assert(CloudSavePolicy.Decide(journal, cloudSignature, manifest) == CloudSaveDecision.Unchanged,
                "identical verified remote recovers from an interrupted confirmation without uploading again");
            var legacyPending = new CloudSyncJournal { Pending = true, SnapshotId = Guid.NewGuid(), LocalSignature = "new-edit" };
            CloudSavePolicy.Migrate(legacyPending);
            Assert(legacyPending.ConfirmedSnapshotId == Guid.Empty &&
                CloudSavePolicy.Decide(legacyPending, "new-edit", manifest) == CloudSaveDecision.Conflict,
                "legacy pending journal with unknown baseline must not guess a winner");
            log.AppendLine("[PASS] Cloud save barrier, baseline migration, retries and explicit conflict resolution");
            var altered = Path.Combine(scratch, "tampered.zip");
            using (var output = ZipFile.Open(altered, ZipArchiveMode.Create))
            using (var input = ZipFile.OpenRead(archive))
            {
                foreach (var entry in input.Entries)
                {
                    using var destinationStream = output.CreateEntry(entry.FullName).Open();
                    if (entry.FullName == "templates/example.json")
                    {
                        var bytes = Encoding.UTF8.GetBytes("{\"format\":\"corrupt\"}");
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
