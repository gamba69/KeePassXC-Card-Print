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
    private static string Fingerprint(CloudConnectionProfile profile)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            profile, CloudSyncJsonContext.Default.CloudConnectionProfile)));

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
            return record is not null && record.VerifiedUtc != default &&
                string.Equals(record.ProfileHash, Fingerprint(profile), StringComparison.Ordinal);
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
        // Guard against verification of a profile that was concurrently changed.
        if (Fingerprint(profile) != Fingerprint(Load()))
            throw new InvalidOperationException("Connection settings changed during verification.");
        Directory.CreateDirectory(SettingsLocation.AppDataRoot);
        var temp = VerificationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(
                new CloudConnectionVerification { ProfileHash = Fingerprint(profile),
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
        var changed = !string.Equals(Fingerprint(previous), Fingerprint(profile), StringComparison.Ordinal);
        Directory.CreateDirectory(SettingsLocation.AppDataRoot);
        var temp = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(profile,
                CloudSyncJsonContext.Default.CloudConnectionProfile));
            File.Move(temp, PathName, true);
            if (changed) InvalidateVerification();
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
    private string _status = "Pending";
    private string _detail = "";
    public string Status => _status;
    public string Detail => _detail;
    public DateTime LastSuccessfulUtc => _journal.LastSuccessfulUtc;
    public Func<Task<bool>>? BeforeRemoteApplyAsync { get; set; }
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
                return JsonSerializer.Deserialize(File.ReadAllBytes(path),
                    CloudSyncJsonContext.Default.CloudSyncJournal) ?? new();
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
        Changed?.Invoke();
    }
    public void MarkPending()
    {
        if (_disposed) return;
        // A close caused only by window geometry, printer settings or other
        // machine-local values must not trigger an upload.
        try
        {
            var current = CloudConfigurationArchive.Signature(_root);
            if (current == _journal.LocalSignature) return;
            _journal.Pending = true;
            SaveJournal();
        }
        catch (IOException)
        {
            _journal.Pending = true;
            SaveJournal();
        }
    }
    private void OnFileRenamed(object sender, RenamedEventArgs e) => OnFileChanged(sender, e);
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed || _applying || SettingsLocation.Mode != ConfigurationStorage.Cloud) return;
        var name = Path.GetRelativePath(_root, e.FullPath).Replace('\\', '/');
        if (!CloudConfigurationArchive.IsPortableChange(name)) return;
        // The next synchronization computes the full portable signature. A crash
        // before debounce is also detected by signature comparison at startup.
        SetStatus("Pending");
        _debounce.Change(TimeSpan.FromMilliseconds(900), Timeout.InfiniteTimeSpan);
    }
    public void Start()
    {
        if (_disposed) return;
        _poll.Change(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(90));
        RunBackground();
    }
    public void Stop()
    {
        _poll.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _debounce.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }
    private void RunBackground()
    {
        if (_disposed || SettingsLocation.Mode != ConfigurationStorage.Cloud) return;
        _ = Task.Run(async () =>
        {
            try { await SynchronizeAsync(_lifetime.Token); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception) { /* ExecuteAsync already published Offline/Error. */ }
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

    public Task SynchronizeAsync(CancellationToken ct = default) => ExecuteAsync(false, false, ct);
    public Task UploadCurrentAsync(CancellationToken ct = default) => ExecuteAsync(true, false, ct);
    public Task DownloadExistingAsync(CancellationToken ct = default) => ExecuteAsync(false, true, ct);

    private async Task ExecuteAsync(bool forceUpload, bool forceDownload, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        var scratch = Path.Combine(Path.GetTempPath(), "kpc-cloud-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(scratch);
            var profile = CloudConnectionStore.Load();
            if (!CloudConnectionStore.IsVerified(profile))
            {
                SetStatus("Pending", "Cloud connection needs verification in Settings.");
                throw new InvalidOperationException("Cloud connection is not verified.");
            }
            if (profile.Provider.Length == 0)
            {
                SetStatus("Error", "Cloud provider is not configured.");
                throw new InvalidOperationException("Cloud provider is not configured.");
            }
            _journal = LoadJournal();
            var signature = CloudConfigurationArchive.Signature(_root);
            UpdateLocalJournal(signature);
            using var transport = CloudTransportFactory.Create(CloudTransportAdapter.Options(profile),
                CloudTransportAdapter.Services());
            var remoteFile = Path.Combine(scratch, "remote.zip");
            var remoteStage = Path.Combine(scratch, "stage");
            CloudArchiveManifest? remote = null;
            var remoteExists = await RetryAsync(() => transport.DownloadAsync(
                CloudConnectionStore.RemoteFilePath(profile), remoteFile, ct), ct);
            if (remoteExists)
            {
                SetStatus("Downloading");
                remote = CloudConfigurationArchive.ReadVerified(remoteFile, remoteStage);
            }
            if (forceDownload && remote is null)
                throw new FileNotFoundException("Cloud configuration does not exist.");
            var same = remote is not null && remote.SnapshotId == _journal.SnapshotId &&
                !_journal.Pending;
            if (same && !forceUpload)
            {
                _journal.LastSuccessfulUtc = DateTime.UtcNow;
                SaveJournal();
                SetStatus("Synced");
                return;
            }
            var applyRemote = RemoteWins(remote, _journal, forceUpload, forceDownload);
            if (applyRemote)
            {
                if (BeforeRemoteApplyAsync is not null && !await BeforeRemoteApplyAsync())
                {
                    SetStatus("Pending", "Finish editing the current mapping before applying the cloud snapshot.");
                    return;
                }
                SetStatus("Downloading");
                _applying = true;
                try
                {
                    CloudConfigurationArchive.ApplyVerified(_root, remoteStage, remote!);
                    _journal.SnapshotId = remote!.SnapshotId;
                    _journal.SavedAtUtc = remote.SavedAtUtc;
                    _journal.LocalSignature = CloudConfigurationArchive.Signature(_root);
                    _journal.Pending = false;
                    _journal.LastSuccessfulUtc = DateTime.UtcNow;
                    SaveJournal();
                }
                finally { _applying = false; }
                RemoteApplied?.Invoke();
                SetStatus("Synced");
                return;
            }
            SetStatus("Uploading");
            var outgoing = Path.Combine(scratch, "outgoing.zip");
            var published = CloudConfigurationArchive.Create(_root, outgoing,
                _journal.SnapshotId, _journal.SavedAtUtc);
            await RetryAsync(async () =>
            {
                await transport.EnsureDirectoryAsync(CloudConnectionStore.RemoteFolder(profile), ct);
                await transport.UploadAsync(outgoing, CloudConnectionStore.RemoteFilePath(profile),
                    CloudWriteMode.Replace, ct);
                return true;
            }, ct);
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
            // Do not clear pending when a file was edited during the upload.
            if (string.Equals(CloudConfigurationArchive.Signature(_root), signature, StringComparison.Ordinal))
            {
                _journal.Pending = false;
                _journal.LocalSignature = signature;
            }
            _journal.LastSuccessfulUtc = DateTime.UtcNow;
            SaveJournal();
            SetStatus(_journal.Pending ? "Pending" : "Synced");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (CloudTransportException ex)
        {
            SetStatus(ex.Kind is CloudFailureKind.Transient or CloudFailureKind.RateLimited
                ? "Offline" : "Error", ex.Message);
            throw;
        }
        catch (IOException ex) { SetStatus("Offline", ex.Message); throw; }
        catch (Exception ex) { SetStatus("Error", ex.Message); throw; }
        finally
        {
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
            catch (IOException) { }
            _gate.Release();
        }
    }
    internal static bool RemoteWins(CloudArchiveManifest? remote,
        CloudSyncJournal local, bool forceUpload = false, bool forceDownload = false) =>
        remote is not null && !forceUpload &&
        (forceDownload || remote.SavedAtUtc >= local.SavedAtUtc);

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
    public static CloudTransportServices Services() =>
        PlatformServices.Current.Cloud.CreateServices();

    // Never touch configuration.zip while testing. A random independent object
    // verifies authentication, folder creation, upload, download and deletion.
    public static async Task VerifyAsync(CloudConnectionProfile profile, CancellationToken ct)
    {
        var folder = CloudConnectionStore.RemoteFolder(profile);
        var testFile = folder + "/.connection-test-" + Guid.NewGuid().ToString("N") + ".bin";
        var scratch = Path.Combine(Path.GetTempPath(), "kpc-cloud-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        using var transport = CloudTransportFactory.Create(Options(profile), Services());
        var uploaded = false;
        try
        {
            var source = Path.Combine(scratch, "source.bin");
            var result = Path.Combine(scratch, "result.bin");
            File.WriteAllBytes(source, RandomNumberGenerator.GetBytes(1024));
            await transport.EnsureDirectoryAsync(folder, ct);
            await transport.UploadAsync(source, testFile, CloudWriteMode.CreateNew, ct);
            uploaded = true;
            if (!await transport.DownloadAsync(testFile, result, ct) || !File.Exists(result) ||
                !CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(File.ReadAllBytes(source)), SHA256.HashData(File.ReadAllBytes(result))))
                throw new InvalidDataException("Cloud connection verification failed (SHA-256).");
            await transport.DeleteAsync(testFile, ct);
            uploaded = false;
            CloudConnectionStore.MarkVerified(profile);
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
    public static async Task AuthenticateAsync(CloudConnectionProfile profile, CancellationToken ct)
    {
        var options = Options(profile);
        CloudConnectionStore.InvalidateVerification();
        if (options.Provider == CloudProvider.GoogleDrive)
        {
            using var http = new HttpClient { Timeout = options.Timeout };
            await new GoogleOAuthSession(options, Services(), http).LoginAsync(ct);
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
