using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeePassXC.CloudTransport;
using KeePassXCCardPrint.Platforms;

namespace KeePassXCCardPrint.Services;

public sealed class CloudConnectionProfile
{
    public string Provider { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string Region { get; set; } = "auto";
    public string GoogleClientId { get; set; } = "";
    public string ProtonExecutable { get; set; } = "";
    public string RemoteFolder { get; set; } = "KeePassXC-Card-Print";
    public bool CheckProtonCliAutomatically { get; set; } = true;
}

// The verification token is local, bound to the exact saved connection profile.
// It must never be included in the cloud configuration archive.
public sealed class CloudConnectionVerification
{
    public string ProfileHash { get; set; } = "";
    public DateTime VerifiedUtc { get; set; }
}

public sealed class CloudSyncJournal
{
    public Guid SnapshotId { get; set; }
    public DateTime SavedAtUtc { get; set; }
    public string LocalSignature { get; set; } = "";
    public DateTime LastSuccessfulUtc { get; set; }
    public bool Pending { get; set; }
    // The confirmed remote baseline survives creation of a new local snapshot.
    public Guid ConfirmedSnapshotId { get; set; }
    public string ConfirmedSignature { get; set; } = "";
    // Bound destination. A connectivity check must never select a new upload target.
    public string RemoteTargetHash { get; set; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CloudConnectionProfile))]
[JsonSerializable(typeof(CloudSyncJournal))]
[JsonSerializable(typeof(CloudConnectionVerification))]
internal partial class CloudSyncJsonContext : JsonSerializerContext { }

public static class CloudConnectionStore
{
    // Installed Google OAuth clients are public clients. These are application
    // identifiers, never refresh/access tokens or Windows account credentials.
    public const string DefaultGoogleClientId = "165156708668-7v58e5mdea8mojvio0l8u21ihpe71nch.apps.googleusercontent.com";
    public const string DefaultGoogleClientSecret = "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";
    public const string DefaultRemoteFolder = "KeePassXC-Card-Print";
    public const string CredentialTarget = "KeePassXC-Card-Print/CloudConnection";
    public const string GoogleSecretTarget = "KeePassXC-Card-Print/GoogleClientSecret";
    public const string GoogleRefreshTarget = "KeePassXC-Card-Print/GoogleRefreshToken";
    private static string PathName => Path.Combine(SettingsLocation.AppDataRoot, "cloud-connection.json");
    private static string VerificationPath => Path.Combine(SettingsLocation.AppDataRoot, "cloud-verified.json");

    public static string RemoteFolder(CloudConnectionProfile profile)
    {
        var raw = string.IsNullOrWhiteSpace(profile.RemoteFolder)
            ? DefaultRemoteFolder : profile.RemoteFolder.Trim().Trim('/');
        if (raw.Length is < 1 or > 256 || raw.IndexOfAny(new[] { '\\', ':', '\0', '?', '#' }) >= 0 ||
            raw.Split('/').Any(segment => segment.Length is < 1 or > 100 ||
                segment is "." or ".." || segment.EndsWith(' ') ||
                segment.Any(char.IsControl)))
            throw new ArgumentException("Cloud folder must be a safe relative path.");
        return raw;
    }
    public static string RemoteFilePath(CloudConnectionProfile profile) => RemoteFolder(profile) + "/configuration.zip";

    // Identity of the remote storage location, not presentation/CLI/update options.
    // Credentials and tokens are deliberately never part of this fingerprint.
    public static string TargetFingerprint(CloudConnectionProfile profile)
    {
        var location = string.Join("\n", profile.Provider, profile.Endpoint.Trim().TrimEnd('/'),
            profile.Bucket.Trim(), profile.Region.Trim(), RemoteFolder(profile),
            string.IsNullOrWhiteSpace(profile.GoogleClientId)
                ? DefaultGoogleClientId : profile.GoogleClientId.Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(location)));
    }
    // Hash of the current full profile, used only to detect unsaved UI changes.
    internal static string ProfileFingerprint(CloudConnectionProfile profile)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            profile, CloudSyncJsonContext.Default.CloudConnectionProfile)));

    internal static string ConnectionFingerprint(CloudConnectionProfile profile)
    {
        // The Proton CLI update-check preference does not change authentication,
        // endpoints or remote data. It must never invalidate verified access.
        var stable = new CloudConnectionProfile
        {
            Provider = profile.Provider,
            Endpoint = profile.Endpoint,
            Bucket = profile.Bucket,
            Region = profile.Region,
            GoogleClientId = profile.GoogleClientId,
            ProtonExecutable = profile.ProtonExecutable,
            RemoteFolder = profile.RemoteFolder,
            CheckProtonCliAutomatically = true
        };
        return ProfileFingerprint(stable);
    }

    internal static bool SameConnection(CloudConnectionProfile a, CloudConnectionProfile b)
        => string.Equals(ConnectionFingerprint(a), ConnectionFingerprint(b), StringComparison.Ordinal);

    internal static bool SameProfile(CloudConnectionProfile a, CloudConnectionProfile b)
        => string.Equals(ProfileFingerprint(a), ProfileFingerprint(b), StringComparison.Ordinal);

    internal static bool VerificationMatches(CloudConnectionVerification record, CloudConnectionProfile profile)
        => record.VerifiedUtc != default &&
            string.Equals(record.ProfileHash, ConnectionFingerprint(profile), StringComparison.Ordinal);

    // S3 and Cloudflare R2 share one UI choice, but retain distinct endpoints.
    internal static string UiProvider(string provider)
        => provider == "CloudflareR2" ? "S3" : provider;

    internal static string ProviderForSelection(string selected, CloudConnectionProfile saved, string endpoint)
    {
        if (selected == "S3" && saved.Provider == "CloudflareR2" &&
            Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            uri.Host.EndsWith(".r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase))
            return "CloudflareR2";
        return selected;
    }

    public static bool IsVerified(CloudConnectionProfile profile)
    {
        if (string.IsNullOrEmpty(profile.Provider)) return false;
        try
        {
            RemoteFolder(profile);
            if (!File.Exists(VerificationPath) || new FileInfo(VerificationPath).Length > 4096)
                return false;
            var record = JsonSerializer.Deserialize(File.ReadAllBytes(VerificationPath),
                CloudSyncJsonContext.Default.CloudConnectionVerification);
            return record is not null && VerificationMatches(record, profile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
            JsonException or ArgumentException) { return false; }
    }
    public static bool IsCurrentVerified() => IsVerified(Load());
    public static void InvalidateVerification()
    {
        if (File.Exists(VerificationPath)) File.Delete(VerificationPath);
    }
    public static void MarkVerified(CloudConnectionProfile profile)
    {
        // Guard against actual connection edits, not unrelated update preferences.
        if (!SameConnection(profile, Load()))
            throw new InvalidOperationException("Connection settings changed during verification.");
        Directory.CreateDirectory(SettingsLocation.AppDataRoot);
        var temp = VerificationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(
                new CloudConnectionVerification { ProfileHash = ConnectionFingerprint(profile),
                    VerifiedUtc = DateTime.UtcNow },
                CloudSyncJsonContext.Default.CloudConnectionVerification));
            File.Move(temp, VerificationPath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static CloudConnectionProfile Load()
    {
        try
        {
            if (File.Exists(PathName) && new FileInfo(PathName).Length <= 16_384)
                return JsonSerializer.Deserialize(File.ReadAllBytes(PathName),
                    CloudSyncJsonContext.Default.CloudConnectionProfile) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return new();
    }
    public static void Save(CloudConnectionProfile profile)
    {
        if (profile.Provider is not ("GoogleDrive" or "ProtonDrive" or "WebDAV" or "S3" or "CloudflareR2"))
            throw new ArgumentException("Unknown cloud provider.");
        RemoteFolder(profile);
        if (profile.Endpoint.Length > 2048 || profile.Bucket.Length > 256 ||
            profile.Region.Length > 128 || profile.GoogleClientId.Length > 512 ||
            profile.ProtonExecutable.Length > 2048)
            throw new ArgumentException("Cloud settings exceed limits.");
        var previous = Load();
        var connectionChanged = !SameConnection(previous, profile);
        // Preserve verification when only local presentation preferences change.
        var wasVerified = !connectionChanged && IsVerified(previous);
        Directory.CreateDirectory(SettingsLocation.AppDataRoot);
        var temp = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(profile,
                CloudSyncJsonContext.Default.CloudConnectionProfile));
            File.Move(temp, PathName, true);
            if (connectionChanged) InvalidateVerification();
            else if (wasVerified && !IsVerified(profile)) MarkVerified(profile);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

// CloudTransport owns ALL provider HTTP/CLI/auth logic. No parallel remote operations.
public sealed class CloudConfigurationSync : IDisposable
{
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _debounce;
    private readonly System.Threading.Timer _poll;
    private CloudSyncJournal _journal;
    private volatile bool _applying;
    private bool _disposed;
    private volatile bool _started;
    private string _status = "Pending";
    private string _detail = "";
    private CloudTransferProgress? _lastTransferProgress;
    private CloudActivity _activity = new("Idle", "", DateTime.MinValue);
    internal CloudActivity Activity => Volatile.Read(ref _activity);
    internal bool IsStarted => _started;
    public string Status => _status;
    public string Detail => _detail;
    public CloudTransferProgress? LastTransferProgress => Volatile.Read(ref _lastTransferProgress);

    private sealed class TransferObserver(CloudConfigurationSync owner) : IProgress<CloudTransferProgress>
    {
        public void Report(CloudTransferProgress progress) => owner.OnTransferProgress(progress);
    }
    private void OnTransferProgress(CloudTransferProgress progress)
    {
        if (_disposed) return;
        Volatile.Write(ref _lastTransferProgress, progress);
        try { Changed?.Invoke(); }
        catch (Exception ex) { CloudSyncDiagnostics.Record("transfer-notification", ex); }
    }
    public DateTime LastSuccessfulUtc => _journal.LastSuccessfulUtc;
    public Func<Task<bool>>? BeforeRemoteApplyAsync { get; set; }
    // The UI can run the final signature check and application without allowing
    // another editor action between those two operations.
    internal Func<Action, Task>? ApplyOnUiAsync { get; set; }
    public Action? RemoteApplied { get; set; }
    public event Action? Changed;

    public CloudConfigurationSync()
    {
        _root = SettingsLocation.CloudRoot;
        Directory.CreateDirectory(_root);
        _journal = LoadJournal();
        _debounce = new System.Threading.Timer(_ => RunBackground(), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _poll = new System.Threading.Timer(_ => RunBackground(), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _watcher = new FileSystemWatcher(_root, "*.json")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Error += (_, e) =>
        {
            if (_disposed) return;
            CloudSyncDiagnostics.Record("file-watcher", e.GetException());
            SetStatus("Error", "File watcher reported an error; use Sync now to retry.");
        };
        _watcher.EnableRaisingEvents = true;
        // Start after the MainWindow has installed its pre-apply safety gate.
    }
    private string JournalPath => Path.Combine(SettingsLocation.AppDataRoot, "cloud-sync-journal.json");
    private CloudSyncJournal LoadJournal()
    {
        try
        {
            var path = Path.Combine(SettingsLocation.AppDataRoot, "cloud-sync-journal.json");
            if (File.Exists(path) && new FileInfo(path).Length <= 16_384)
            {
                var journal = JsonSerializer.Deserialize(File.ReadAllBytes(path),
                    CloudSyncJsonContext.Default.CloudSyncJournal) ?? new();
                CloudSavePolicy.Migrate(journal);
                return journal;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return new();
    }
    private void SaveJournal()
    {
        Directory.CreateDirectory(SettingsLocation.AppDataRoot);
        var temp = JournalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(_journal,
                CloudSyncJsonContext.Default.CloudSyncJournal));
            File.Move(temp, JournalPath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private void SetStatus(string status, string detail = "")
    {
        _status = status;
        _detail = detail;
        try { Changed?.Invoke(); }
        catch (Exception ex) { CloudSyncDiagnostics.Record("status-notification", ex); }
    }
    private void SetActivity(string stage, string detail = "")
    {
        Volatile.Write(ref _activity, new CloudActivity(stage, detail, DateTime.UtcNow));
        try { Changed?.Invoke(); }
        catch (Exception ex) { CloudSyncDiagnostics.Record("activity-notification", ex); }
    }
    internal bool HasUnconfirmedChanges()
    {
        // The journal is atomically replaced. Signature comparison also detects
        // saves for which FileSystemWatcher has not delivered an event yet.
        return CloudSavePolicy.NeedsUpload(LoadJournal(), CloudConfigurationArchive.Signature(_root));
    }
    internal async Task FlushPendingAsync(CancellationToken ct)
    {
        // ExecuteAsync serializes with the existing transfer. Re-check under
        // the gate, rather than trusting an earlier successful status string.
        do
        {
            ct.ThrowIfCancellationRequested();
            await ExecuteAsync(false, false, ct, null, flushOnly: true);
        } while (HasUnconfirmedChanges());
    }
    private void OnFileRenamed(object sender, RenamedEventArgs e) => OnFileChanged(sender, e);
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed || _applying || SettingsLocation.Mode != ConfigurationStorage.Cloud) return;
        try
        {
            var name = Path.GetRelativePath(_root, e.FullPath).Replace('\\', '/');
            if (!CloudConfigurationArchive.IsPortableChange(name)) return;
            // Ignore private UI-shell changes and duplicate notifications.
            if (!HasUnconfirmedChanges()) return;
            if (Activity.Stage is "Idle" or "Success" or "Pending") SetActivity("Pending");
            if (_started)
                _debounce.Change(TimeSpan.FromMilliseconds(900), Timeout.InfiniteTimeSpan);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or IOException or
            UnauthorizedAccessException or ArgumentException)
        { CloudSyncDiagnostics.Record("file-change", ex); }
    }
    public void Start()
    {
        if (_disposed) return;
        if (CloudMappingRecovery.ReviewRequired)
        {
            _started = false;
            SetStatus("Pending", "Mapping recovery requires review before manual synchronization.");
            SetActivity("Error", "Mapping recovery requires review before manual synchronization.");
            return;
        }
        _started = true;
        try { _poll.Change(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(90)); }
        catch (ObjectDisposedException) { return; }
        RunBackground();
    }
    public void Stop()
    {
        _started = false;
        if (_disposed) return;
        try
        {
            _poll.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _debounce.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException) { /* Window is closing. */ }
    }
    private void RunBackground()
    {
        if (_disposed || !_started || SettingsLocation.Mode != ConfigurationStorage.Cloud) return;
        _ = Task.Run(async () =>
        {
            try { await ExecuteAsync(false, false, _lifetime.Token, null, background: true); }
            catch (OperationCanceledException) when (_disposed || !_started || _lifetime.IsCancellationRequested) { }
            catch (Exception ex) { CloudSyncDiagnostics.Record("background-sync", ex); }
        });
    }
    private void UpdateLocalJournal(string signature)
    {
        if (_journal.SnapshotId == Guid.Empty ||
            !string.Equals(_journal.LocalSignature, signature, StringComparison.Ordinal))
        {
            _journal.SnapshotId = Guid.NewGuid();
            _journal.SavedAtUtc = DateTime.UtcNow;
            _journal.LocalSignature = signature;
            _journal.Pending = true;
            SaveJournal();
        }
    }

    // Serialize repair with network work so no upload races the local replacement.
    public async Task<string> RecoverMappingAsync(string? validSource, string recoveryRoot,
        CancellationToken ct = default)
    {
        var fullRoot = Path.GetFullPath(recoveryRoot);
        var cloudRoot = Path.GetFullPath(_root);
        var activeRoot = Path.GetFullPath(SettingsLocation.DirectoryPath);
        if (!string.Equals(fullRoot, cloudRoot, PlatformServices.Current.FileNameComparison) &&
            !string.Equals(fullRoot, activeRoot, PlatformServices.Current.FileNameComparison))
            throw new ArgumentException("Mapping recovery is limited to the current storage and cloud cache.", nameof(recoveryRoot));
        Stop();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        await _gate.WaitAsync(linked.Token);
        try
        {
            if (_disposed) throw new OperationCanceledException(linked.Token);
            var backupFolder = Path.Combine(SettingsLocation.AppDataRoot, "backups");
            var backup = CloudMappingRecovery.Restore(fullRoot, backupFolder, validSource);
            // Failed transitions may have copied the same malformed file into
            // cloud-cache. Preserve that original separately, then repair the
            // cache with the approved rules, preventing a second failure later.
            if (!string.Equals(fullRoot, cloudRoot, PlatformServices.Current.FileNameComparison) &&
                CloudMappingRecovery.NeedsRecovery(cloudRoot))
            {
                var cacheBackup = CloudMappingRecovery.Restore(cloudRoot, backupFolder,
                    Path.Combine(fullRoot, "mapping.json"));
                backup += Environment.NewLine + cacheBackup;
            }
            SetStatus("Pending");
            return backup;
        }
        finally { _gate.Release(); }
    }

    public Task SynchronizeAsync(CancellationToken ct = default, IProgress<string>? progress = null)
        => ExecuteAsync(false, false, ct, progress);
    public Task UploadCurrentAsync(CancellationToken ct = default, IProgress<string>? progress = null)
        => ExecuteAsync(true, false, ct, progress);
    public Task DownloadExistingAsync(CancellationToken ct = default, IProgress<string>? progress = null)
        => ExecuteAsync(false, true, ct, progress);

    public bool RequiresDestinationChoice(CloudConnectionProfile profile)
    {
        if (profile.Provider.Length == 0) return false;
        var journal = LoadJournal();
        return journal.RemoteTargetHash != CloudConnectionStore.TargetFingerprint(profile);
    }

    // Current journals must be explicitly bound to the same remote destination.
    internal static bool DestinationMatches(CloudSyncJournal journal,
        CloudConnectionProfile profile, CloudArchiveManifest? remote)
    {
        return journal.RemoteTargetHash == CloudConnectionStore.TargetFingerprint(profile);
    }

    // Test/authentication and all uploads/downloads share the same gate. A second
    // command can never alter the target while a network operation is running.
    public async Task VerifyConnectionAsync(CloudConnectionProfile profile,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        await _gate.WaitAsync(linked.Token);
        try
        {
            if (_disposed) throw new OperationCanceledException(linked.Token);
            Volatile.Write(ref _lastTransferProgress, null);
            SetStatus("Checking");
            await CloudTransportAdapter.VerifyAsync(profile, linked.Token, progress,
                new TransferObserver(this));
            SetStatus("Pending");
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            SetStatus("Error", "Cloud connection test failed: " + ex.GetType().Name);
            CloudSyncDiagnostics.Record("connection-test", ex);
            throw;
        }
        finally { _gate.Release(); }
    }
    public async Task AuthenticateConnectionAsync(CloudConnectionProfile profile,
        string language, bool darkTheme, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        await _gate.WaitAsync(linked.Token);
        try
        {
            if (_disposed) throw new OperationCanceledException(linked.Token);
            SetStatus("Checking");
            await CloudTransportAdapter.AuthenticateAsync(profile, linked.Token, language, darkTheme);
            SetStatus("Pending");
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            SetStatus("Error", "Cloud authentication failed: " + ex.GetType().Name);
            CloudSyncDiagnostics.Record("authentication", ex);
            throw;
        }
        finally { _gate.Release(); }
    }

    private async Task ExecuteAsync(bool forceUpload, bool forceDownload, CancellationToken ct,
        IProgress<string>? progress, bool background = false, bool flushOnly = false)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        await _gate.WaitAsync(linked.Token);
        ct = linked.Token;
        var scratch = Path.Combine(Path.GetTempPath(), "kpc-cloud-" + Guid.NewGuid().ToString("N"));
        bool dirty = false, realTransfer = false;
        try
        {
            if (_disposed) throw new OperationCanceledException(ct);
            if (background && !_started) return;
            _journal = LoadJournal();
            var signature = CloudConfigurationArchive.Signature(_root);
            dirty = CloudSavePolicy.NeedsUpload(_journal, signature);
            if (flushOnly && !dirty) return;
            if (dirty && Activity.Stage is "Idle" or "Success" or "Pending") SetActivity("Pending");
            if (flushOnly && CloudMappingRecovery.ReviewRequired)
                throw new InvalidOperationException("Mapping recovery requires review in Settings.");
            Volatile.Write(ref _lastTransferProgress, null);
            progress?.Report("sync.prepare");
            Directory.CreateDirectory(scratch);
            var profile = CloudConnectionStore.Load();
            if (!CloudConnectionStore.IsVerified(profile))
                throw new InvalidOperationException("Cloud connection is not verified.");
            if (profile.Provider.Length == 0)
                throw new InvalidOperationException("Cloud provider is not configured.");
            if (!forceUpload && !forceDownload && !DestinationMatches(_journal, profile, null))
            {
                progress?.Report("sync.destination");
                throw new InvalidOperationException("Cloud destination requires explicit selection in Settings.");
            }
            using var transport = CloudTransportFactory.Create(CloudTransportAdapter.Options(profile),
                CloudTransportAdapter.Services(new TransferObserver(this)));
            progress?.Report("sync.remote");
            SetStatus("Checking");
            var remoteFile = Path.Combine(scratch, "remote.zip");
            var remoteStage = Path.Combine(scratch, "stage");
            CloudArchiveManifest? remote = null;
            // An explicit overwrite is the only path allowed to ignore a broken remote archive.
            if (!forceUpload)
            {
                if (forceDownload) { realTransfer = true; SetActivity("Downloading"); }
                var remoteExists = await RetryAsync(() => transport.DownloadAsync(
                    CloudConnectionStore.RemoteFilePath(profile), remoteFile, ct), ct);
                if (remoteExists) remote = CloudConfigurationArchive.ReadVerified(remoteFile, remoteStage);
            }
            if (background && !_started) return; // stopped while inspecting, before changing data
            if (forceDownload && remote is null)
                throw new FileNotFoundException("Cloud configuration does not exist.");
            // Re-read after network I/O; a new save may have arrived while checking.
            signature = CloudConfigurationArchive.Signature(_root);
            dirty = CloudSavePolicy.NeedsUpload(_journal, signature);
            var decision = CloudSavePolicy.Decide(_journal, signature, remote, forceUpload, forceDownload);
            if (decision == CloudSaveDecision.Conflict)
            {
                UpdateLocalJournal(signature);
                SetActivity("Conflict", "Both local and cloud configuration changed.");
                throw new CloudSaveConflictException();
            }
            if (decision == CloudSaveDecision.Unchanged)
            {
                CloudSavePolicy.Confirm(_journal, remote!, signature, signature);
                _journal.RemoteTargetHash = CloudConnectionStore.TargetFingerprint(profile);
                _journal.LastSuccessfulUtc = DateTime.UtcNow;
                SaveJournal();
                if (!background) CloudMappingRecovery.ClearReviewRequired();
                if (dirty) SetActivity("Success"); // recovery after an unconfirmed upload
                else if (Activity.Stage is "Pending" or "Error" or "Conflict") SetActivity("Idle");
                SetStatus("Synced");
                return;
            }
            if (decision == CloudSaveDecision.Download)
            {
                // A shutdown flush never silently replaces local files.
                if (flushOnly) throw new CloudSaveConflictException();
                if (BeforeRemoteApplyAsync is not null && !await BeforeRemoteApplyAsync())
                {
                    SetStatus("Pending", "Finish editing the current mapping before applying the cloud snapshot.");
                    if (realTransfer) SetActivity(dirty ? "Pending" : "Idle");
                    if (!background) throw new InvalidOperationException("Finish editing the current template first.");
                    return;
                }
                if (background && !_started) return;
                void Apply()
                {
                    ct.ThrowIfCancellationRequested();
                    if (background && !_started) return;
                    if (CloudConfigurationArchive.Signature(_root) != signature)
                        throw new CloudSaveConflictException();
                    realTransfer = true;
                    SetActivity("Downloading");
                    SetStatus("Downloading");
                    progress?.Report("sync.apply");
                    _applying = true;
                    try
                    {
                        CloudConfigurationArchive.ApplyVerified(_root, remoteStage, remote!);
                        CloudSavePolicy.Confirm(_journal, remote!, CloudSavePolicy.ManifestSignature(remote!),
                            CloudConfigurationArchive.Signature(_root));
                        _journal.RemoteTargetHash = CloudConnectionStore.TargetFingerprint(profile);
                        _journal.LastSuccessfulUtc = DateTime.UtcNow;
                        SaveJournal();
                    }
                    finally { _applying = false; }
                    RemoteApplied?.Invoke();
                    if (!background) CloudMappingRecovery.ClearReviewRequired();
                    SetActivity(_journal.Pending ? "Pending" : "Success");
                    SetStatus(_journal.Pending ? "Pending" : "Synced");
                }
                if (ApplyOnUiAsync is not null) await ApplyOnUiAsync(Apply);
                else Apply();
                return;
            }
            // Persist the pending snapshot before initiating any write. Keep the
            // exact archive signature, not the earlier pre-network signature.
            UpdateLocalJournal(signature);
            realTransfer = true;
            SetActivity("Uploading");
            SetStatus("Uploading");
            progress?.Report("sync.upload");
            var outgoing = Path.Combine(scratch, "outgoing.zip");
            var published = CloudConfigurationArchive.Create(_root, outgoing,
                _journal.SnapshotId, _journal.SavedAtUtc);
            var publishedSignature = CloudSavePolicy.ManifestSignature(published);
            _journal.LocalSignature = publishedSignature;
            _journal.Pending = true;
            SaveJournal();
            await RetryAsync(async () =>
            {
                await transport.EnsureDirectoryAsync(CloudConnectionStore.RemoteFolder(profile), ct);
                await transport.UploadAsync(outgoing, CloudConnectionStore.RemoteFilePath(profile),
                    CloudWriteMode.Replace, ct);
                return true;
            }, ct);
            SetActivity("Verifying");
            progress?.Report("sync.confirm");
            var confirmation = Path.Combine(scratch, "confirmation.zip");
            if (!await RetryAsync(() => transport.DownloadAsync(
                CloudConnectionStore.RemoteFilePath(profile), confirmation, ct), ct))
                throw new IOException("Cloud verification download failed.");
            var confirmationStage = Path.Combine(scratch, "verified");
            var verified = CloudConfigurationArchive.ReadVerified(confirmation, confirmationStage);
            if (verified.SnapshotId != published.SnapshotId ||
                !CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(File.ReadAllBytes(confirmation)), SHA256.HashData(File.ReadAllBytes(outgoing))))
                throw new InvalidDataException("Cloud upload verification failed.");
            CloudSavePolicy.Confirm(_journal, published, publishedSignature,
                CloudConfigurationArchive.Signature(_root));
            _journal.RemoteTargetHash = CloudConnectionStore.TargetFingerprint(profile);
            _journal.LastSuccessfulUtc = DateTime.UtcNow;
            SaveJournal();
            if (!background && !_journal.Pending) CloudMappingRecovery.ClearReviewRequired();
            SetActivity(_journal.Pending ? "Pending" : "Success");
            SetStatus(_journal.Pending ? "Pending" : "Synced");
            if (_journal.Pending && _started)
                _debounce.Change(TimeSpan.FromMilliseconds(50), Timeout.InfiniteTimeSpan);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (dirty || realTransfer) SetActivity("Error", "Cloud operation cancelled or timed out.");
            throw;
        }
        catch (CloudSaveConflictException ex)
        {
            SetActivity("Conflict", ex.Message);
            SetStatus("Error", ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            // A failed ordinary poll need not claim a local save failed.
            if (dirty || realTransfer || flushOnly || Activity.Stage == "Pending") SetActivity("Error", ex.Message);
            SetStatus(ex is CloudTransportException transportError &&
                transportError.Kind is CloudFailureKind.Transient or CloudFailureKind.RateLimited || ex is IOException
                ? "Offline" : "Error", ex.Message);
            throw;
        }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { CloudSyncDiagnostics.Record("temporary-cleanup", ex); }
            _gate.Release();
        }
    }

    private static async Task<T> RetryAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        for (var attempt = 0; ; ++attempt)
        {
            ct.ThrowIfCancellationRequested();
            try { return await work(); }
            catch (CloudTransportException ex) when (
                attempt < 2 && ex.Kind is CloudFailureKind.Transient or CloudFailureKind.RateLimited)
            { await Task.Delay(TimeSpan.FromSeconds(1 << attempt), ct); }
            catch (IOException) when (attempt < 2)
            { await Task.Delay(TimeSpan.FromSeconds(1 << attempt), ct); }
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _started = false;
        _watcher.Dispose();
        _debounce.Dispose();
        _poll.Dispose();
        _lifetime.Cancel();
        // Do not dispose the semaphore while an asynchronous upload owns it.
    }
}

internal static class CloudTransportAdapter
{
    public static CloudConnectionOptions Options(CloudConnectionProfile profile) => new()
    {
        Provider = profile.Provider switch
        {
            "GoogleDrive" => CloudProvider.GoogleDrive,
            "ProtonDrive" => CloudProvider.ProtonDrive,
            "WebDAV" => CloudProvider.WebDav,
            "S3" => CloudProvider.S3,
            "CloudflareR2" => CloudProvider.CloudflareR2,
            _ => throw new InvalidOperationException("Unsupported cloud provider.")
        },
        RemotePrefix = "",
        Endpoint = string.IsNullOrWhiteSpace(profile.Endpoint) ? null : profile.Endpoint,
        Bucket = string.IsNullOrWhiteSpace(profile.Bucket) ? null : profile.Bucket,
        S3Region = profile.Region,
        CredentialTarget = CloudConnectionStore.CredentialTarget,
        GoogleClientId = string.IsNullOrWhiteSpace(profile.GoogleClientId)
            ? CloudConnectionStore.DefaultGoogleClientId : profile.GoogleClientId,
        GoogleClientSecret = string.IsNullOrWhiteSpace(profile.GoogleClientId)
            ? CloudConnectionStore.DefaultGoogleClientSecret : null,
        GoogleClientSecretTarget = string.IsNullOrWhiteSpace(profile.GoogleClientId)
            ? null : CloudConnectionStore.GoogleSecretTarget,
        GoogleRefreshTokenTarget = CloudConnectionStore.GoogleRefreshTarget,
        ProtonCliExecutable = profile.Provider == "ProtonDrive"
            ? PlatformServices.Current.Cloud.ResolveProtonExecutable(profile.ProtonExecutable)
            : profile.ProtonExecutable,
        Timeout = TimeSpan.FromSeconds(75)
    };
    public static CloudTransportServices Services(IProgress<CloudTransferProgress>? progress = null)
    {
        var services = PlatformServices.Current.Cloud.CreateServices();
        return progress is null ? services : services with { TransferProgress = progress };
    }

    // Never touch configuration.zip while testing. A random independent object
    // verifies authentication, folder creation, upload, download and deletion.
    public static async Task VerifyAsync(CloudConnectionProfile profile, CancellationToken ct,
        IProgress<string>? progress = null,
        IProgress<CloudTransferProgress>? transferProgress = null)
    {
        using var transport = CloudTransportFactory.Create(Options(profile), Services(transferProgress));
        await VerifyWithTransportAsync(profile, transport, ct, progress, recordVerification: true);
    }
    // Test seam: an isolated in-memory store proves the check never reads or
    // modifies configuration.zip, without real cloud credentials or network.
    internal static async Task VerifyWithTransportAsync(CloudConnectionProfile profile,
        ICloudTransport transport, CancellationToken ct, IProgress<string>? progress = null,
        bool recordVerification = false)
    {
        progress?.Report("test.connect");
        var folder = CloudConnectionStore.RemoteFolder(profile);
        var testFile = folder + "/.connection-test-" + Guid.NewGuid().ToString("N") + ".bin";
        var scratch = Path.Combine(Path.GetTempPath(), "kpc-cloud-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var uploaded = false;
        try
        {
            var source = Path.Combine(scratch, "source.bin");
            var result = Path.Combine(scratch, "result.bin");
            File.WriteAllBytes(source, RandomNumberGenerator.GetBytes(1024));
            await transport.EnsureDirectoryAsync(folder, ct);
            progress?.Report("test.upload");
            await transport.UploadAsync(source, testFile, CloudWriteMode.CreateNew, ct);
            uploaded = true;
            progress?.Report("test.download");
            if (!await transport.DownloadAsync(testFile, result, ct) || !File.Exists(result) ||
                !CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(File.ReadAllBytes(source)), SHA256.HashData(File.ReadAllBytes(result))))
                throw new InvalidDataException("Cloud connection verification failed (SHA-256).");
            progress?.Report("test.cleanup");
            await transport.DeleteAsync(testFile, ct);
            uploaded = false;
            if (recordVerification) CloudConnectionStore.MarkVerified(profile);
            progress?.Report("test.complete");
        }
        finally
        {
            // Do not silently mark a connection verified if cleanup fails.
            if (uploaded)
            {
                try { await transport.DeleteAsync(testFile, CancellationToken.None); }
                catch (Exception) { /* Manual cleanup can be retried by the user. */ }
            }
            try { Directory.Delete(scratch, recursive: true); }
            catch (IOException) { }
        }
    }
    public static async Task AuthenticateAsync(CloudConnectionProfile profile, CancellationToken ct,
        string language = "auto", bool darkTheme = true)
    {
        var options = Options(profile);
        CloudConnectionStore.InvalidateVerification();
        if (options.Provider == CloudProvider.GoogleDrive)
        {
            using var http = new HttpClient { Timeout = options.Timeout };
            var services = Services() with
            {
                GoogleOAuthCallbackPages = CloudOAuthCallbackPages.Create(language, darkTheme)
            };
            await new GoogleOAuthSession(options, services, http).LoginAsync(ct);
        }
        else if (options.Provider == CloudProvider.ProtonDrive)
        {
            using var transport = CloudTransportFactory.Create(options, Services());
            if (!await ((ICloudAuthentication)transport).LoginAsync(ct))
                throw new InvalidOperationException("Proton login was cancelled.");
        }
        else
            throw new InvalidOperationException("Configure the connection credentials and run Test.");
    }
}
