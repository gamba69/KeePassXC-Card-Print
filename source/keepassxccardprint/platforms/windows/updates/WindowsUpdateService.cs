using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sigstore;
using Tuf;

namespace KeePassXCCardPrint.Platforms.Windows.Updates;

internal sealed record UpdatePreferences(bool CheckAutomatically=true,
    bool DownloadAutomatically=true,string Installation="exit");
internal sealed record UpdateSnapshot(string Status="idle",string Version="",
    string Detail="",bool Unverified=false,bool Reinstall=false);

// Independent of vault, printing and user configuration. Only a verified,
// allow-listed portable release is ever handed to the external helper.
internal sealed class WindowsUpdateService : IDisposable
{
    private const string Repo=CardPrintUpdatePolicy.Repository;
    private const string Latest="https://api.github.com/repos/"+Repo+"/releases/latest";
    private const string Api="https://api.github.com/repos/"+Repo+"/";
    private const string TufBootstrap="https://raw.githubusercontent.com/cli/cli/3d5a9e355a837f2f5ee78d7484c118cc1342ad1a/pkg/cmd/attestation/verification/embed/tuf-repo.github.com/root.json";
    private const string TufBootstrapSha256="98cba97be9075bc98b2322de3de85fbd1b70ec7392991dfd2f53d215bede1a8d";
    private const long MaxResponse=3*1024*1024;
    internal static string DefaultRoot => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData),"KeePassXCCardPrint","updates");
    private readonly string _root;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly Func<string,string,string,CancellationToken,Task> _verifyAttestation;
    private string? _ready;
    private bool _readyRepair;
    private bool _disposed;
    public UpdatePreferences Preferences {get;private set;}
    public UpdateSnapshot Snapshot {get;private set;}=new();
    public bool IsReady => _ready is not null;
    public bool IsRepairReady => IsReady && _readyRepair;
    public event Action? Changed;
    private void Publish(UpdateSnapshot s) {Snapshot=s;Changed?.Invoke();}

    internal WindowsUpdateService(HttpMessageHandler? handler=null,string? root=null,
        Func<string,string,string,CancellationToken,Task>? verifyAttestation=null)
    {
        _root=root??DefaultRoot;
        _http=handler is null?new HttpClient():new HttpClient(handler);
        _http.Timeout=TimeSpan.FromMinutes(3);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("KeePassXCCardPrint-Updater/7.0");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _verifyAttestation=verifyAttestation??VerifyImmutableReleaseAsync;
        Preferences=ReadPreferences();
        RestorePending();
    }

    private string PrefPath=>Path.Combine(_root,"preferences.txt");
    private UpdatePreferences ReadPreferences()
    {
        try
        {
            if(!File.Exists(PrefPath))return new();
            var lines=File.ReadAllLines(PrefPath);
            return lines.Length==3?ValidatePreferences(new(lines[0]=="true",lines[1]=="true",lines[2])):new();
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){return new();}
    }
    private static UpdatePreferences ValidatePreferences(UpdatePreferences value) => value with
    { Installation=value.Installation is "exit" or "ask" or "manual"?value.Installation:"exit" };
    internal void SavePreferences(UpdatePreferences value)
    {
        value=ValidatePreferences(value);
        Directory.CreateDirectory(_root);
        var tmp=Path.Combine(_root,".settings-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            File.WriteAllText(tmp,(value.CheckAutomatically?"true":"false")+"\n"+
                (value.DownloadAutomatically?"true":"false")+"\n"+value.Installation+"\n",new UTF8Encoding(false));
            File.Move(tmp,PrefPath,true);
            Preferences=value;Changed?.Invoke();
        }
        finally{if(File.Exists(tmp))File.Delete(tmp);}
    }

    private void RestorePending()
    {
        try
        {
            var pointer=Path.Combine(_root,"ready.txt");
            if(!File.Exists(pointer))return;
            var lines=File.ReadAllLines(pointer);
            if(lines.Length!=2 || lines[1] is not ("update" or "repair"))return;
            var tag=lines[0];
            var repair=lines[1]=="repair";
            if(!CardPrintUpdatePolicy.IsEligible(tag,AppBuildInfo.Version,repair))return;
            var directory=Path.Combine(_root,"pending-"+CardPrintUpdatePolicy.ParseVersion(tag).ToString(3));
            VerifyPayloadManifest(directory);
            if(!string.Equals(File.ReadAllText(Path.Combine(directory,"verified.txt")).Trim(),tag,
                StringComparison.Ordinal))return;
            // A repair is authorized only after a fresh, explicit confirmation.
            // Never auto-install a persisted repair after a subsequent restart.
            _ready=repair?null:directory;
            _readyRepair=false;
            if(_ready is not null)
                Publish(new("ready",tag,"",File.Exists(Path.Combine(directory,"unverified.txt"))));
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidDataException or OverflowException) { /* Never trust broken staging. */ }
    }

    internal async Task CheckAsync(bool manual,CancellationToken ct=default,bool reinstall=false)
    {
        if(reinstall && !manual)throw new ArgumentException("Repair must be requested interactively.",nameof(reinstall));
        if(!manual && !Preferences.CheckAutomatically)return;
        if(manual)await _gate.WaitAsync(ct);
        else if(!await _gate.WaitAsync(0,ct))return;
        try
        {
            var cached=IsReady && Snapshot.Status=="ready"?Snapshot:null;
            Publish(new("checking"));
            using var release=await GetJsonAsync(Latest,ct);
            var json=release.RootElement;
            if(json.GetProperty("draft").GetBoolean() || json.GetProperty("prerelease").GetBoolean())
                throw new InvalidDataException("Latest release is not stable.");
            var tag=json.GetProperty("tag_name").GetString()??"";
            if(!CardPrintUpdatePolicy.IsEligible(tag,AppBuildInfo.Version,reinstall))
            {Publish(cached??new("current"));return;}
            bool repair=!CardPrintUpdatePolicy.IsNewer(tag,AppBuildInfo.Version);
            bool immutable=json.GetProperty("immutable").ValueKind switch
            {
                JsonValueKind.True=>true,JsonValueKind.False=>false,
                _=>throw new InvalidDataException("GitHub did not report release immutability.")
            };
            if(repair && !immutable)
                throw new InvalidDataException("Recovery requires an immutable GitHub release.");
            var archive=CardPrintUpdatePolicy.ZipName(tag);
            var checksum=CardPrintUpdatePolicy.ChecksumName(tag);
            JsonElement zip=default,sha=default;
            foreach(var asset in json.GetProperty("assets").EnumerateArray())
            {
                if(asset.GetProperty("name").GetString()==archive)zip=asset;
                if(asset.GetProperty("name").GetString()==checksum)sha=asset;
            }
            if(zip.ValueKind==JsonValueKind.Undefined || sha.ValueKind==JsonValueKind.Undefined)
                throw new InvalidDataException("Release ZIP or its SHA-256 asset is missing.");
            var zipUrl=CheckedAssetUrl(zip,tag,archive);
            var shaUrl=CheckedAssetUrl(sha,tag,checksum);
            var warning=immutable?"":"GitHub Immutable Releases is disabled; authenticity is not verified.";
            if(_ready is not null && cached?.Version==tag && _readyRepair==repair &&
                (!immutable || !File.Exists(Path.Combine(_ready,"unverified.txt"))))
            {Publish(new("ready",tag,warning,!immutable,repair));return;}
            Publish(new("available",tag,warning,!immutable,repair));
            if(!manual && !Preferences.DownloadAutomatically)return;
            // Staging is invalidated *before* any network/download operation.
            _ready=null;_readyRepair=false;
            var stage=Path.Combine(_root,"pending-"+CardPrintUpdatePolicy.ParseVersion(tag).ToString(3));
            if(Directory.Exists(stage))Directory.Delete(stage,true);
            Directory.CreateDirectory(stage);
            try
            {
                var archivePath=Path.Combine(stage,archive);
                await DownloadAsync(zipUrl,archivePath,CardPrintUpdatePolicy.MaxZipBytes,ct);
                var checksumText=Encoding.UTF8.GetString(await GetBytesAsync(shaUrl,4096,ct));
                var digest=zip.TryGetProperty("digest",out var item) && item.ValueKind==JsonValueKind.String
                    ?item.GetString():null;
                CardPrintUpdatePolicy.CheckChecksum(archivePath,checksumText,archive,digest);
                if(immutable)
                {
                    try {await _verifyAttestation(tag,archive,CardPrintUpdatePolicy.HashFile(archivePath),ct);}
                    catch(Exception ex) when(ex is not OperationCanceledException)
                    {throw new InvalidDataException("GitHub attestation verification failed: "+ex.Message,ex);}
                }
                CardPrintUpdatePolicy.Extract(archivePath,Path.Combine(stage,"payload"));
                WritePayloadManifest(stage);
                if(!immutable)File.WriteAllText(Path.Combine(stage,"unverified.txt"),"mutable");
                File.WriteAllText(Path.Combine(stage,"verified.txt"),tag);
                File.WriteAllLines(Path.Combine(_root,"ready.txt"),new[]{tag,repair?"repair":"update"});
                _ready=stage;_readyRepair=repair;
                Publish(new("ready",tag,warning,!immutable,repair));
            }
            catch {if(Directory.Exists(stage))Directory.Delete(stage,true);throw;}
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch(Exception ex){Publish(new("error","",ex.Message));}
        finally{_gate.Release();}
    }

    private static Uri CheckedAssetUrl(JsonElement asset,string tag,string filename)
    {
        var url=asset.GetProperty("browser_download_url").GetString()??"";
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme!="https" ||
            uri.Host!="github.com" || uri.UserInfo.Length!=0 ||
            uri.AbsolutePath!=$"/{Repo}/releases/download/{tag}/{filename}")
            throw new InvalidDataException("Invalid release asset URL.");
        return uri;
    }
    private async Task<JsonDocument> GetJsonAsync(string url,CancellationToken ct) =>
        JsonDocument.Parse(await GetBytesAsync(new Uri(url),MaxResponse,ct));
    private async Task<byte[]> GetBytesAsync(Uri url,long limit,CancellationToken ct)
    {
        if(url.Scheme!=Uri.UriSchemeHttps)throw new InvalidDataException("HTTPS is required.");
        using var response=await _http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct);
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength is {} known && known>limit)
            throw new InvalidDataException("Response too large.");
        await using var stream=await response.Content.ReadAsStreamAsync(ct);
        using var result=new MemoryStream();
        var buffer=new byte[65536];int count;
        while((count=await stream.ReadAsync(buffer,ct))>0)
        {
            if(result.Length+count>limit)throw new InvalidDataException("Response too large.");
            result.Write(buffer,0,count);
        }
        return result.ToArray();
    }
    private async Task DownloadAsync(Uri url,string path,long limit,CancellationToken ct)
    {
        using var response=await _http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct);
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength is {} size && size>limit)
            throw new InvalidDataException("Archive too large.");
        await using var source=await response.Content.ReadAsStreamAsync(ct);
        await using var destination=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        var buffer=new byte[65536];int count;
        while((count=await source.ReadAsync(buffer,ct))>0)
        {
            if(destination.Length+count>limit)throw new InvalidDataException("Archive too large.");
            await destination.WriteAsync(buffer.AsMemory(0,count),ct);
        }
    }

    // Same GitHub-attested release verification as Cloud Sync: TUF pin,
    // Fulcio identity, authenticated timestamps and a matching DSSE subject.
    private async Task VerifyImmutableReleaseAsync(string tag,string zipName,string zipDigest,CancellationToken ct)
    {
        using var tagJson=await GetJsonAsync(Api+"git/ref/tags/"+Uri.EscapeDataString(tag),ct);
        var gitSha=tagJson.RootElement.GetProperty("object").GetProperty("sha").GetString()??"";
        if(gitSha.Length!=40 || !gitSha.All(Uri.IsHexDigit))
            throw new InvalidDataException("GitHub tag object SHA-1 is invalid.");
        using var attestations=await GetJsonAsync(Api+"attestations/sha1:"+gitSha+
            "?predicate_type=release&per_page=100",ct);
        var list=attestations.RootElement.GetProperty("attestations");
        if(list.ValueKind!=JsonValueKind.Array || list.GetArrayLength()==0)
            throw new InvalidDataException("Immutable release has no attestation.");
        var bootstrap=await GetBytesAsync(new Uri(TufBootstrap),MaxResponse,ct);
        if(!string.Equals(Convert.ToHexString(SHA256.HashData(bootstrap)),TufBootstrapSha256,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Pinned GitHub TUF bootstrap changed.");
        var tufUri=new Uri("https://tuf-repo.github.com/");
        using var tuf=new TufClient(new TufClientOptions
        {
            MetadataBaseUrl=tufUri,TargetsBaseUrl=new Uri(tufUri,"targets/"),TrustedRoot=bootstrap
        });
        var target=await tuf.GetTargetAsync("trusted_root.json",ct);
        if(target.Metadata.Expires<=DateTimeOffset.UtcNow)
            throw new InvalidDataException("GitHub TUF metadata expired.");
        var trust=new InMemoryTrustRootProvider(
            GithubTrustRootCompatibility.ParseVerifiedTarget(target.Content));
        var verifier=new SigstoreVerifier(trust);
        var policy=new VerificationPolicy
        {
            CertificateIdentity=new CertificateIdentity
            { SubjectAlternativeName="https://dotcom.releases.github.com" },
            RequireTransparencyLog=false,RequireSignedTimestamps=true,
            RequireSignedCertificateTimestamps=false
        };
        int matches=0;
        foreach(var entry in list.EnumerateArray())
        {
            if(!entry.TryGetProperty("initiator",out var initiator) || initiator.GetString()!="github")
                continue;
            byte[] bundleBytes;
            if(entry.TryGetProperty("bundle_url",out var bundleUrl) &&
                bundleUrl.ValueKind==JsonValueKind.String &&
                bundleUrl.GetString() is {Length:>0} address)
            {
                if(!Uri.TryCreate(address,UriKind.Absolute,out var uri) || uri.Scheme!="https")
                    throw new InvalidDataException("Invalid attestation bundle URL.");
                bundleBytes=RawSnappy.Decode(await GetBytesAsync(uri,MaxResponse,ct),4*1024*1024);
            }
            else if(entry.TryGetProperty("bundle",out var embedded))
                bundleBytes=Encoding.UTF8.GetBytes(embedded.GetRawText());
            else throw new InvalidDataException("Missing attestation bundle.");
            using var parsed=JsonDocument.Parse(bundleBytes);
            if(!parsed.RootElement.TryGetProperty("dsseEnvelope",out var envelope))continue;
            var payload=envelope.GetProperty("payload").GetString()??"";
            var statement=Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var statementJson=JsonDocument.Parse(statement);
            if(!statementJson.RootElement.TryGetProperty("predicate",out var predicate) ||
                !predicate.TryGetProperty("tag",out var signedTag) || signedTag.GetString()!=tag)
                continue;
            var bundle=SigstoreBundle.Deserialize(Encoding.UTF8.GetString(bundleBytes));
            await verifier.VerifyAsync(ReadOnlyMemory<byte>.Empty,bundle,policy,ct);
            CardPrintUpdatePolicy.VerifySignedStatement(statement,tag,gitSha,zipName,zipDigest);
            matches++;
        }
        if(matches!=1)throw new InvalidDataException("Expected exactly one valid GitHub release attestation.");
    }

    // Staged manifests contain relative paths and hashes, never absolute
    // paths. Recursive enumeration is mandatory (fonts/licenses in particular).
    internal static void WritePayloadManifest(string stage)
    {
        var root=Path.Combine(stage,"payload");
        var names=Directory.GetFiles(root,"*",SearchOption.AllDirectories)
            .Select(x=>Path.GetRelativePath(root,x).Replace('\\','/')).ToArray();
        if(!names.Contains(CardPrintUpdatePolicy.Executable,StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Staged executable missing.");
        foreach(var name in names)CardPrintUpdatePolicy.SafePath(root,name);
        File.WriteAllLines(Path.Combine(stage,"files.txt"),
            names.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase)
                .Select(name=>name+"\t"+CardPrintUpdatePolicy.HashFile(CardPrintUpdatePolicy.SafePath(root,name))),
            new UTF8Encoding(false));
    }
    internal static Dictionary<string,string> ReadManifest(string path)
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var line in File.ReadAllLines(path))
        {
            var pair=line.Split('\t');
            if(pair.Length!=2 || !CardPrintUpdatePolicy.IsOwnedFile(pair[0]) ||
                pair[1].Length!=64 || !pair[1].All(Uri.IsHexDigit) ||
                !result.TryAdd(pair[0],pair[1]))
                throw new InvalidDataException("Unsafe or duplicate manifest entry.");
        }
        if(!result.ContainsKey(CardPrintUpdatePolicy.Executable))
            throw new InvalidDataException("Executable missing from manifest.");
        return result;
    }
    internal static void VerifyPayloadManifest(string stage)
    {
        var root=Path.Combine(stage,"payload");
        if(!File.Exists(Path.Combine(stage,"verified.txt")) ||
            !File.Exists(Path.Combine(stage,"files.txt")))
            throw new InvalidDataException("Verified staging manifest missing.");
        var expected=ReadManifest(Path.Combine(stage,"files.txt"));
        foreach(var item in expected)
        {
            var file=CardPrintUpdatePolicy.SafePath(root,item.Key);
            if(!File.Exists(file) || !string.Equals(CardPrintUpdatePolicy.HashFile(file),item.Value,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Staged payload modified.");
        }
        var actual=Directory.GetFiles(root,"*",SearchOption.AllDirectories)
            .Select(x=>Path.GetRelativePath(root,x).Replace('\\','/')).ToArray();
        if(actual.Length!=expected.Count || actual.Any(x=>!expected.ContainsKey(x)))
            throw new InvalidDataException("Unexpected staged file.");
    }

    internal void ValidateInstallation()
    {
        if(_ready is null)throw new InvalidOperationException("No staged update.");
        VerifyPayloadManifest(_ready);
        var current=Environment.ProcessPath??throw new IOException("No current EXE path.");
        if(!Path.GetFileName(current).Equals(CardPrintUpdatePolicy.Executable,
            StringComparison.OrdinalIgnoreCase))throw new IOException("Unknown executable name.");
        var target=Path.GetDirectoryName(current)!;
        if((new DirectoryInfo(target).Attributes&FileAttributes.ReparsePoint)!=0)
            throw new IOException("Installation directory must not be a reparse point.");
        var stageRoot=Path.Combine(_ready,"payload");
        var names=ReadManifest(Path.Combine(_ready,"files.txt"));
        foreach(var name in names.Keys)CardPrintUpdatePolicy.SafePath(target,name);
        var bytes=names.Keys.Sum(name=>new FileInfo(CardPrintUpdatePolicy.SafePath(stageRoot,name)).Length);
        if(new DriveInfo(Path.GetPathRoot(target)!).AvailableFreeSpace<Math.Max(bytes*2,50L*1024*1024))
            throw new IOException("Insufficient free disk space for update and rollback.");
        var probe=Path.Combine(target,".kpc-update-"+Guid.NewGuid().ToString("N"));
        try{using(File.Create(probe)){} }
        finally{if(File.Exists(probe))File.Delete(probe);}
    }

    internal void LaunchInstaller(bool restart,bool confirmedRepair)
    {
        if(IsRepairReady && !confirmedRepair)
            throw new InvalidOperationException("Repair needs explicit confirmation.");
        ValidateInstallation();
        var stage=_ready!;
        if(!string.Equals(File.ReadAllText(Path.Combine(stage,"verified.txt")).Trim(),Snapshot.Version,
            StringComparison.Ordinal))throw new InvalidDataException("Staged version mismatch.");
        var current=Environment.ProcessPath!;
        var target=Path.GetDirectoryName(current)!;
        var helper=Path.Combine(stage,"update-helper.exe");
        File.Copy(current,helper,true);
        foreach(var file in Directory.GetFiles(target,"*.dll"))
        {
            var name=Path.GetFileName(file);
            if(CardPrintUpdatePolicy.IsOwnedFile(name))
                File.Copy(file,Path.Combine(stage,name),true);
        }
        using var process=Process.GetCurrentProcess();
        var psi=new ProcessStartInfo(helper){UseShellExecute=false,CreateNoWindow=true};
        foreach(var arg in new[]{"--apply-update",target,stage,
            process.Id.ToString(CultureInfo.InvariantCulture),
            process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            restart?"1":"0"})psi.ArgumentList.Add(arg);
        if(Process.Start(psi) is null)throw new IOException("Cannot start update helper.");
    }

    private static void WaitForOwner(string pidText,string ticksText)
    {
        if(!int.TryParse(pidText,NumberStyles.None,CultureInfo.InvariantCulture,out int pid) ||
            !long.TryParse(ticksText,NumberStyles.None,CultureInfo.InvariantCulture,out long ticks))
            throw new InvalidDataException("Invalid parent-process identity.");
        try
        {
            using var process=Process.GetProcessById(pid);
            if(process.StartTime.ToUniversalTime().Ticks!=ticks || !process.WaitForExit(120000))
                throw new IOException("Parent process did not exit.");
        }
        catch(ArgumentException){/* Process already exited. */}
    }
    private static bool StageInsideRoot(string stage,string root) =>
        Path.GetFullPath(stage).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+
            Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    internal static int ApplyUpdate(string[] args)
    {
        if(args.Length!=6 || args[0]!="--apply-update" || args[5] is not ("0" or "1"))return 2;
        try
        {
            var target=Path.GetFullPath(args[1]);var stage=Path.GetFullPath(args[2]);
            var root=DefaultRoot;
            if(!StageInsideRoot(stage,root) ||
                !File.Exists(Path.Combine(target,CardPrintUpdatePolicy.Executable)))return 3;
            WaitForOwner(args[3],args[4]);
            ApplyVerifiedFiles(stage,target,root,validateNew:()=>
                SmokeTest(Path.Combine(target,CardPrintUpdatePolicy.Executable)));
            if(args[5]=="1")Process.Start(new ProcessStartInfo(
                Path.Combine(target,CardPrintUpdatePolicy.Executable)){UseShellExecute=false});
            return 0;
        }
        catch(Exception){return 1;}
    }
    internal static bool LaunchInterruptedRecovery(string[] startupArgs)
    {
        var root=DefaultRoot;var journal=Path.Combine(root,"installing.txt");
        if(!File.Exists(journal))return false;
        var stage=Path.GetFullPath(File.ReadAllText(journal).Trim());
        if(!StageInsideRoot(stage,root))throw new IOException("Invalid recovery stage.");
        var target=Path.GetFullPath(File.ReadAllText(Path.Combine(stage,"target.txt")).Trim());
        if(!string.Equals(target,Path.GetDirectoryName(Environment.ProcessPath),
            StringComparison.OrdinalIgnoreCase))
            throw new IOException("Recovery does not match executable directory.");
        var helper=Path.Combine(stage,"update-helper.exe");
        if(!File.Exists(helper))throw new IOException("Recovery helper missing.");
        using var process=Process.GetCurrentProcess();
        var psi=new ProcessStartInfo(helper){UseShellExecute=false,CreateNoWindow=true};
        foreach(var arg in new[]{"--recover-update",stage,target,
            process.Id.ToString(CultureInfo.InvariantCulture),
            process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)})
            psi.ArgumentList.Add(arg);
        foreach(var arg in startupArgs)if(arg!="--reinstall")psi.ArgumentList.Add(arg);
        return Process.Start(psi) is not null;
    }
    internal static int ApplyRecovery(string[] args)
    {
        if(args.Length<5 || args[0]!="--recover-update")return 2;
        try
        {
            var stage=Path.GetFullPath(args[1]);var target=Path.GetFullPath(args[2]);
            var root=DefaultRoot;
            if(!StageInsideRoot(stage,root) ||
                !string.Equals(File.ReadAllText(Path.Combine(stage,"target.txt")).Trim(),target,
                    StringComparison.OrdinalIgnoreCase))return 3;
            WaitForOwner(args[3],args[4]);
            RestoreBackup(stage,target,root);
            File.Delete(Path.Combine(root,"installing.txt"));
            File.WriteAllText(Path.Combine(root,"completed.txt"),stage);
            var psi=new ProcessStartInfo(Path.Combine(target,CardPrintUpdatePolicy.Executable))
                {UseShellExecute=false};
            foreach(var arg in args.Skip(5))psi.ArgumentList.Add(arg);
            return Process.Start(psi) is null?6:0;
        }
        catch(Exception){return 1;}
    }
    private static bool SmokeTest(string exe)
    {
        var info=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true};
        info.ArgumentList.Add("--selftest-update");
        using var process=Process.Start(info);
        if(process is null)return false;
        if(!process.WaitForExit(30000))
        { process.Kill(entireProcessTree:true);process.WaitForExit(5000);return false; }
        return process.ExitCode==0;
    }

    internal static string OwnedManifestPath(string root,string target)
    {
        var normalized=Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar).ToUpperInvariant();
        return Path.Combine(root,"owned",Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))+".txt");
    }
    private static Dictionary<string,string> ReadOwned(string path)
    {
        if(!File.Exists(path))return new(StringComparer.OrdinalIgnoreCase);
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
            throw new InvalidDataException("Owned manifest is a reparse point.");
        return ReadManifest(path);
    }
    private static void WriteOwned(string from,string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if((File.GetAttributes(Path.GetDirectoryName(destination)!)&FileAttributes.ReparsePoint)!=0)
            throw new InvalidDataException("Owned-manifest directory is a reparse point.");
        var tmp=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.Copy(from,tmp);File.Move(tmp,destination,true);}
        finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    private static bool Unmodified(string target,string name,string hash)
    {
        var file=CardPrintUpdatePolicy.SafePath(target,name);
        return File.Exists(file) && string.Equals(CardPrintUpdatePolicy.HashFile(file),hash,
            StringComparison.OrdinalIgnoreCase);
    }
    private static void RestoreOwned(string stage,string root,string target)
    {
        var destination=OwnedManifestPath(root,target);
        var source=Path.Combine(stage,"owned-before.txt");
        if(File.Exists(source))WriteOwned(source,destination);
        else if(File.Exists(destination))File.Delete(destination);
    }

    internal static void ApplyVerifiedFiles(string stage,string target,string root,
        Action<string>? beforeReplace=null,Func<bool>? validateNew=null,
        Action<string>? beforeDelete=null)
    {
        VerifyPayloadManifest(stage);
        stage=Path.GetFullPath(stage);target=Path.GetFullPath(target);root=Path.GetFullPath(root);
        if(!File.Exists(Path.Combine(target,CardPrintUpdatePolicy.Executable)))
            throw new InvalidDataException("Application target is missing.");
        if(File.Exists(Path.Combine(root,"installing.txt")))
            throw new IOException("An interrupted update must be recovered first.");
        var files=ReadManifest(Path.Combine(stage,"files.txt"));
        var payload=Path.Combine(stage,"payload");
        var ownedPath=OwnedManifestPath(root,target);
        var owned=ReadOwned(ownedPath);
        var obsolete=owned.Where(item=>!files.ContainsKey(item.Key) &&
            Unmodified(target,item.Key,item.Value)).Select(item=>item.Key)
            .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
        // Preflight path safety before creating the journal (so no user-file
        // writes occur before a complete, immutable file set is known).
        foreach(var name in files.Keys.Concat(obsolete))CardPrintUpdatePolicy.SafePath(target,name);
        var backup=Path.Combine(stage,"backup");
        if(Directory.Exists(backup))Directory.Delete(backup,true);
        Directory.CreateDirectory(backup);
        var ownedBefore=Path.Combine(stage,"owned-before.txt");
        if(File.Exists(ownedBefore))File.Delete(ownedBefore);
        if(File.Exists(ownedPath))File.Copy(ownedPath,ownedBefore);
        var originals=new List<string>();
        foreach(var name in files.Keys.Concat(obsolete).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var old=CardPrintUpdatePolicy.SafePath(target,name);
            if(!File.Exists(old))continue;
            var destination=CardPrintUpdatePolicy.SafePath(backup,name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(old,destination,true);
            originals.Add(name);
        }
        File.WriteAllLines(Path.Combine(stage,"original-files.txt"),originals);
        File.WriteAllText(Path.Combine(stage,"target.txt"),target);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,"installing.txt"),stage);
        try
        {
            foreach(var name in files.Keys)
            {
                beforeReplace?.Invoke(name);
                var from=CardPrintUpdatePolicy.SafePath(payload,name);
                var destination=CardPrintUpdatePolicy.SafePath(target,name);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var temporary=Path.Combine(Path.GetDirectoryName(destination)!,
                    ".kpc-update-"+Guid.NewGuid().ToString("N")+".tmp");
                try{File.Copy(from,temporary);File.Move(temporary,destination,true);}
                finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
            foreach(var name in obsolete)
            {
                beforeDelete?.Invoke(name);
                if(Unmodified(target,name,owned[name]))File.Delete(CardPrintUpdatePolicy.SafePath(target,name));
            }
            if(validateNew is not null && !validateNew())
                throw new IOException("Updated binary failed startup smoke test.");
            WriteOwned(Path.Combine(stage,"files.txt"),ownedPath);
            var previous=Path.Combine(root,"previous");
            if(Directory.Exists(previous))Directory.Delete(previous,true);
            Directory.Move(backup,previous);
            File.WriteAllText(Path.Combine(root,"completed.txt"),stage);
            File.Delete(Path.Combine(root,"installing.txt"));
        }
        catch
        {
            // Leave installing.txt in place if rollback fails. Startup will
            // launch the old, separately cached helper to retry recovery.
            RestoreBackup(stage,target,root);
            File.Delete(Path.Combine(root,"installing.txt"));
            throw;
        }
    }
    internal static void RestoreBackup(string stage,string target,string root)
    {
        var backup=Path.Combine(stage,"backup");
        if(!Directory.Exists(backup))backup=Path.Combine(root,"previous");
        if(!Directory.Exists(backup))throw new IOException("Rollback backup missing.");
        var original=File.ReadAllLines(Path.Combine(stage,"original-files.txt"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var staged=ReadManifest(Path.Combine(stage,"files.txt"));
        foreach(var name in staged.Keys)
            if(!original.Contains(name))
            {
                var file=CardPrintUpdatePolicy.SafePath(target,name);
                if(File.Exists(file))File.Delete(file);
            }
        foreach(var file in Directory.GetFiles(backup,"*",SearchOption.AllDirectories))
        {
            var name=Path.GetRelativePath(backup,file).Replace('\\','/');
            var destination=CardPrintUpdatePolicy.SafePath(target,name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file,destination,true);
        }
        RestoreOwned(stage,root,target);
    }
    internal void CleanupCompleted()
    {
        try
        {
            var completed=Path.Combine(_root,"completed.txt");
            if(!File.Exists(completed))return;
            var stage=Path.GetFullPath(File.ReadAllText(completed).Trim());
            if(!StageInsideRoot(stage,_root))return;
            if(Directory.Exists(stage))Directory.Delete(stage,true);
            File.Delete(completed);
            var pointer=Path.Combine(_root,"ready.txt");
            if(File.Exists(pointer))File.Delete(pointer);
            if(!File.Exists(Path.Combine(_root,"installing.txt")))
                foreach(var old in Directory.GetDirectories(_root,"pending-*"))
                    Directory.Delete(old,true);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        { /* Best effort, repeat after next successful launch. */ }
    }
    public void Dispose()
    {
        if(_disposed)return;
        _disposed=true;
        _http.CancelPendingRequests();_http.Dispose();
        // A background check may still release _gate in its finally block.
    }
}

// Bounds-checked raw Snappy (GitHub attestation bundle_url responses).
internal static class RawSnappy
{
    internal static byte[] Decode(ReadOnlySpan<byte> data,int maximum)
    {
        int pos=0,shift=0;ulong size=0;
        while(true)
        {
            if(pos>=data.Length || shift>28)throw new InvalidDataException("Snappy length invalid.");
            byte b=data[pos++];size|=(ulong)(b&127)<<shift;
            if((b&128)==0)break;
            shift+=7;
        }
        if(size>(ulong)maximum)throw new InvalidDataException("Snappy output limit.");
        var output=new byte[(int)size];int outPos=0;
        while(pos<data.Length)
        {
            int tag=data[pos++],kind=tag&3,length,offset;
            if(kind==0)
            {
                length=tag>>2;
                if(length<60)length++;
                else
                {
                    int count=length-59;
                    if(count>4 || pos+count>data.Length)throw new InvalidDataException("Snappy literal header.");
                    length=0;
                    for(int i=0;i<count;i++)length|=data[pos++]<<(8*i);
                    length=checked(length+1);
                }
                if(length<0 || length>output.Length-outPos || length>data.Length-pos)
                    throw new InvalidDataException("Snappy literal exceeds bounds.");
                data.Slice(pos,length).CopyTo(output.AsSpan(outPos,length));
                pos+=length;outPos+=length;continue;
            }
            if(kind==1)
            {
                length=4+((tag>>2)&7);
                if(pos>=data.Length)throw new InvalidDataException("Snappy short copy header.");
                offset=((tag&0xE0)<<3)|data[pos++];
            }
            else
            {
                length=1+(tag>>2);
                int count=kind==2?2:4;
                if(pos+count>data.Length)throw new InvalidDataException("Snappy copy offset.");
                long value=0;
                for(int i=0;i<count;i++)value|=(long)data[pos++]<<(8*i);
                if(value>int.MaxValue)throw new InvalidDataException("Snappy offset overflow.");
                offset=(int)value;
            }
            if(offset<=0 || offset>outPos || length>output.Length-outPos)
                throw new InvalidDataException("Snappy copy outside output.");
            for(int i=0;i<length;i++)output[outPos+i]=output[outPos+i-offset];
            outPos+=length;
        }
        if(outPos!=output.Length)throw new InvalidDataException("Snappy truncated.");
        return output;
    }
}
