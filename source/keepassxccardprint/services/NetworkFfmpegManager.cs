using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

namespace KeePassXCCardPrint.Services;

// This manifest is updated only by a trusted Card Print release. Never execute
// a mutable "latest" URL or silently download a third-party binary.
internal static class NetworkFfmpegManager
{
    internal const string Version="9.0.2-982-g836df4e858";
    internal const string ArchiveSha256="f7ba27e7986a2d0835881e742a682dec17896f6dc2cdcf9e596f043d2763d894";
    private const string Tag="autobuild-2026-11-26-12-56";
    internal const string ArchiveUrl="https://github.com/FFmpeg-Builds/FFmpeg-Builds/releases/download/"+Tag+"/ffmpeg-n9.0.2-982-g836df4e858-win64-lgpl-shared-9.0.zip";
    internal const long ArchiveBytes=77_787_475;
    private static string Root=>Path.Combine(SettingsLocation.AppDataRoot,"components","ffmpeg");
    private static string Installed=>Path.Combine(Root,"current");
    internal static string Exe=>Path.Combine(Installed,"ffmpeg.exe");
    private static readonly SemaphoreSlim Gate=new(1,1);
    internal static string? InstalledVersion
    {
        get
        {
            try
            {
                string marker=Path.Combine(Installed,"VERSION.txt");
                if(!File.Exists(Exe)||!File.Exists(marker)||new FileInfo(marker).Length>128)return null;
                return File.ReadAllText(marker).Trim();
            }
            catch{return null;}
        }
    }
    internal static bool IsReady=>InstalledVersion==Version;
    internal sealed class Lease:IDisposable
    {
        private int _disposed;
        public void Dispose(){if(Interlocked.Exchange(ref _disposed,1)==0)Gate.Release();}
    }
    internal static Lease Acquire()
    {
        if(!Gate.Wait(0))throw new IOException("FFmpeg is busy.");
        return new Lease();
    }
    internal static Lease AcquireRuntime()
    {
        var lease=Acquire();
        if(IsReady)return lease;
        lease.Dispose();throw new NetworkCameraException(NetworkCameraState.DecoderMissing);
    }
    internal static async Task InstallAsync(IProgress<double>? progress,CancellationToken ct)
    {
        using var lease=Acquire();
        Directory.CreateDirectory(Root);
        string temp=Path.Combine(Root,"stage-"+Guid.NewGuid().ToString("N"));
        string archive=Path.Combine(temp,"runtime.zip"),stage=Path.Combine(temp,"payload"),backup=Path.Combine(temp,"old");
        Directory.CreateDirectory(temp);Directory.CreateDirectory(stage);
        bool committed=false;
        try
        {
            using var handler=new HttpClientHandler{AllowAutoRedirect=true,MaxAutomaticRedirections=5,UseCookies=false,AutomaticDecompression=DecompressionMethods.None};
            using var http=new HttpClient(handler){Timeout=TimeSpan.FromMinutes(10)};
            using var downloadTimeout=CancellationTokenSource.CreateLinkedTokenSource(ct);downloadTimeout.CancelAfter(TimeSpan.FromMinutes(10));ct=downloadTimeout.Token;
            using var request=new HttpRequestMessage(HttpMethod.Get,ArchiveUrl);
            request.Headers.UserAgent.ParseAdd("KeePassXCCardPrint/"+AppBuildInfo.Version);
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if(response.RequestMessage?.RequestUri?.Scheme!="https"||response.Content.Headers.ContentLength is long length && length!=ArchiveBytes)throw new IOException("Unexpected FFmpeg archive length or transport.");
            await using(var input=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using(var output=new FileStream(archive,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
            using(var sha=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer=new byte[65536];long total=0;int n;
                while((n=await input.ReadAsync(buffer,ct).ConfigureAwait(false))>0)
                {
                    total+=n;if(total>ArchiveBytes)throw new IOException("FFmpeg archive exceeds expected length.");
                    sha.AppendData(buffer,0,n);await output.WriteAsync(buffer.AsMemory(0,n),ct).ConfigureAwait(false);
                    progress?.Report(total/(double)ArchiveBytes*.85);
                }
                if(total!=ArchiveBytes||!string.Equals(Convert.ToHexString(sha.GetHashAndReset()),ArchiveSha256,StringComparison.OrdinalIgnoreCase))throw new IOException("FFmpeg archive SHA-256 mismatch.");
            }
            // Extraction and runtime validation are done off the dispatcher.
            await Task.Run(()=>Extract(archive,stage,ct),ct).ConfigureAwait(false);progress?.Report(.93);
            await ValidateRuntime(stage,ct).ConfigureAwait(false);
            File.WriteAllText(Path.Combine(stage,"VERSION.txt"),Version);
            File.WriteAllText(Path.Combine(stage,"SOURCE.txt"),$"Optional unmodified LGPL shared FFmpeg runtime.\nArchive: {ArchiveUrl}\nSHA-256: {ArchiveSha256}\nFFmpeg source: https://github.com/FFmpeg/FFmpeg/tree/836df4e858\nBuild recipes: https://github.com/FFmpeg-Builds/FFmpeg-Builds/tree/{Tag}\n");
            ct.ThrowIfCancellationRequested();
            if(Directory.Exists(Installed))Directory.Move(Installed,backup);
            try{Directory.Move(stage,Installed);committed=true;}
            catch{if(Directory.Exists(backup)&&!Directory.Exists(Installed))Directory.Move(backup,Installed);throw;}
            progress?.Report(1);
        }
        finally
        {
            // If rollback itself fails, preserve the previous runtime for recovery.
            if(committed||!Directory.Exists(backup))try{Directory.Delete(temp,true);}catch{}
        }
    }
    private static void Extract(string archive,string stage,CancellationToken ct)
    {
        using var zip=ZipFile.OpenRead(archive);
        var binaries=zip.Entries.Where(e=>e.Name.Equals("ffmpeg.exe",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(binaries.Length!=1)throw new IOException("Unexpected FFmpeg archive layout.");
        string prefix=binaries[0].FullName[..^binaries[0].Name.Length];
        var files=zip.Entries.Where(e=>e.FullName.StartsWith(prefix,StringComparison.Ordinal)&&e.FullName==prefix+e.Name&&(e.Name.Equals("ffmpeg.exe",StringComparison.OrdinalIgnoreCase)||e.Name.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))).ToArray();
        if(files.Length<5||files.Length>128||files.Sum(e=>e.Length)>768L*1024*1024)throw new IOException("Unexpected FFmpeg runtime payload.");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in files)
        {
            ct.ThrowIfCancellationRequested();
            if(!names.Add(file.Name)||file.Name.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c is '.' or '-' or '_'))||file.Length<=0||file.Length>256L*1024*1024)throw new IOException("Invalid runtime filename or length.");
            file.ExtractToFile(Path.Combine(stage,file.Name));
        }
        string notices=Path.Combine(stage,"notices");Directory.CreateDirectory(notices);
        int i=0;
        foreach(var entry in zip.Entries.Where(e=>e.Length>0&&e.Length<=2*1024*1024&&
            (e.Name.StartsWith("license",StringComparison.OrdinalIgnoreCase)||e.Name.StartsWith("copying",StringComparison.OrdinalIgnoreCase)||e.Name.StartsWith("copyright",StringComparison.OrdinalIgnoreCase)||e.Name.StartsWith("readme",StringComparison.OrdinalIgnoreCase)||e.FullName.Contains("/licenses/",StringComparison.OrdinalIgnoreCase))))
        {
            ct.ThrowIfCancellationRequested();if(++i>512)throw new IOException("Too many notices.");
            entry.ExtractToFile(Path.Combine(notices,$"{i:D3}.txt"));
        }
        if(i==0)throw new IOException("Missing FFmpeg licensing notices.");
    }
    private static async Task ValidateRuntime(string stage,CancellationToken ct)
    {
        async Task<string> Run(params string[] args)
        {
            var start=new ProcessStartInfo(Path.Combine(stage,"ffmpeg.exe")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=stage,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in args)start.ArgumentList.Add(arg);
            using var process=Process.Start(start)??throw new IOException("FFmpeg failed to start.");
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var kill=timeout.Token.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch{}});
            var output=process.StandardOutput.ReadToEndAsync(timeout.Token);var error=process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var text=(await output.ConfigureAwait(false))+(await error.ConfigureAwait(false));
            if(process.ExitCode!=0)throw new IOException("FFmpeg runtime validation failed.");return text;
        }
        var version=await Run("-hide_banner","-version").ConfigureAwait(false);
        if(!version.Contains("836df4e858",StringComparison.Ordinal))throw new IOException("Unexpected FFmpeg version.");
        var rtsp=await Run("-hide_banner","-h","demuxer=rtsp").ConfigureAwait(false);
        if(!rtsp.Contains("tls_verify",StringComparison.Ordinal)||!rtsp.Contains("verifyhost",StringComparison.Ordinal))throw new IOException("FFmpeg TLS options unavailable.");
    }
    internal static void Remove()
    {
        using var lease=Acquire();if(Directory.Exists(Root))Directory.Delete(Root,true);
    }
}
