using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using SkiaSharp;

namespace KeePassXCCardPrint.Services;

internal enum NetworkCameraState { Connecting, Receiving, Retrying, Stopped, Authentication, Certificate, DecoderMissing, SystemCodecMissing, VideoInitializationFailed, VideoDecodeFailed, Unsupported, Timeout, Error }
internal sealed record NetworkCameraSample(int Bytes, double ReceiveMs, double? IntervalMs, double? Fps, double? Kbps, int Reconnects, string Format, bool Tls, int Width, int Height, double? DecodeMs=null);
internal sealed class NetworkCameraException(NetworkCameraState state,string detail="") : IOException { internal NetworkCameraState State { get; } = state; internal string Detail { get; } = detail; }

internal static class NetworkCameraSource
{
    internal const int MaxFrameBytes = 24 * 1024 * 1024;
    private const long MaxPixels = 32_000_000;
    private static string Decoder => NetworkFfmpegManager.Exe;
    internal static bool HasDecoder => NetworkFfmpegManager.IsReady;

    internal static async Task Run(NetworkCameraProfile profile, Action<SKBitmap,NetworkCameraSample> frame, Action<NetworkCameraState,int> status, CancellationToken ct, bool once = false, Action<string>? diagnostic=null,Action<string>? streamCodec=null)
    {
        if (!profile.Validate(out var uri)) throw new NetworkCameraException(NetworkCameraState.Unsupported);
        bool rtsp = uri.Scheme is "rtsp" or "rtsps";
        bool tls = uri.Scheme is "https" or "rtsps";
        int reconnects = 0;
        if (ct.IsCancellationRequested) return;
        diagnostic?.Invoke("");status(NetworkCameraState.Connecting,reconnects);
        // Retrying covers both the backoff and subsequent connection attempts.
        while (!ct.IsCancellationRequested)
        {
            long previous = 0;
            bool received = false;
            var watch = Stopwatch.StartNew();
            try
            {
                bool Emit(SKBitmap bitmap, int payloadBytes, string format, double elapsed, double? decodeMs)
                {
                    bool handedOff = false;
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        int w=bitmap.Width,h=bitmap.Height;
                        long now = watch.ElapsedMilliseconds;
                        double? interval = received ? Math.Max(1,now-previous) : null;
                        double? fps = interval is double ms ? 1000/ms : null;
                        double? kbps = !rtsp && interval is double dt ? payloadBytes*8.0/dt : null;
                        previous = now;
                        if(!received){diagnostic?.Invoke("");status(NetworkCameraState.Receiving,reconnects);}
                        received = true;
                        frame(bitmap,new(payloadBytes,elapsed,interval,fps,kbps,reconnects,format,tls && !(rtsp && profile.Engine=="ffmpeg"),w,h,decodeMs));
                        handedOff = true;
                        return once;
                    }
                    finally { if (!handedOff) ScannerPixels.ClearAndDispose(bitmap); }
                }
                if (rtsp && profile.Engine=="sharp") await SharpNetworkCameraSource.Run(profile,uri,Emit,ct,streamCodec).ConfigureAwait(false);
                else if (rtsp) await ReadRtsp(profile,uri,Emit,ct).ConfigureAwait(false);
                else await ReadHttp(profile,uri,Emit,ct).ConfigureAwait(false);
                if (once && received) return;
                throw new NetworkCameraException(NetworkCameraState.Error);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                var state = ex switch
                {
                    NetworkCameraException failure => failure.State,
                    OperationCanceledException => NetworkCameraState.Timeout,
                    HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => NetworkCameraState.Certificate,
                    _ => NetworkCameraState.Error
                };
                if (ct.IsCancellationRequested) return;
                diagnostic?.Invoke(ex is NetworkCameraException error?error.Detail:"");
                // Publish only the final outcome, never an intermediate error followed by Retrying.
                if (once || !ShouldReconnect(profile.Reconnect,state))
                {
                    status(state,reconnects);
                    if (once) throw ex is NetworkCameraException known?known:new NetworkCameraException(state);
                    return;
                }
            }
            if (ct.IsCancellationRequested) return;
            status(NetworkCameraState.Retrying,++reconnects);
            try { await Task.Delay(Math.Min(5000,1000+reconnects*500),ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    internal static bool ShouldReconnect(bool enabled,NetworkCameraState state)=>
        enabled&&state is not (NetworkCameraState.Authentication or NetworkCameraState.Certificate or NetworkCameraState.DecoderMissing or NetworkCameraState.SystemCodecMissing or NetworkCameraState.VideoInitializationFailed or NetworkCameraState.VideoDecodeFailed or NetworkCameraState.Unsupported);

    internal static SKBitmap DecodeJpeg(byte[] bytes)
    {
        if(bytes.Length is <4 or >MaxFrameBytes||bytes[0]!=0xff||bytes[1]!=0xd8||bytes[^2]!=0xff||bytes[^1]!=0xd9)throw new NetworkCameraException(NetworkCameraState.Unsupported);
        using var data=SKData.CreateCopy(bytes);
        using var codec=SKCodec.Create(data)??throw new NetworkCameraException(NetworkCameraState.Unsupported);
        if(codec.Info.Width<1||codec.Info.Height<1||(long)codec.Info.Width*codec.Info.Height>MaxPixels)throw new NetworkCameraException(NetworkCameraState.Unsupported);
        return SKBitmap.Decode(codec,new SKImageInfo(codec.Info.Width,codec.Info.Height,SKColorType.Bgra8888,SKAlphaType.Opaque))??throw new NetworkCameraException(NetworkCameraState.Unsupported);
    }
    internal static (int Width,int Height) CheckImage(byte[] bytes)
    {using var bitmap=DecodeJpeg(bytes);return (bitmap.Width,bitmap.Height);}
    private static bool EmitJpeg(byte[] bytes,string format,double elapsed,Func<SKBitmap,int,string,double,double?,bool> emit)
    {
        int size=bytes.Length;SKBitmap bitmap;var timer=Stopwatch.StartNew();
        try{bitmap=DecodeJpeg(bytes);}finally{CryptographicOperations.ZeroMemory(bytes);}
        return emit(bitmap,size,format,elapsed,timer.Elapsed.TotalMilliseconds);
    }
    internal static bool CertificateMatches(X509Certificate2? cert, string pin, SslPolicyErrors errors)
    {
        if (cert is null) return false;
        if (pin.Length == 0) return errors == SslPolicyErrors.None;
        // Pin replaces PKI/name validation only for this configured endpoint, not globally.
        // Still reject an expired or not-yet-valid leaf.
        var now = DateTime.UtcNow;
        return now >= cert.NotBefore.ToUniversalTime() && now <= cert.NotAfter.ToUniversalTime() &&
            string.Equals(cert.GetCertHashString(HashAlgorithmName.SHA256),pin,StringComparison.OrdinalIgnoreCase);
    }
    // An explicit certificate-only probe: no HTTP request, no credentials, no acceptance persisted here.
    internal static async Task<string> ReadPeerPin(Uri uri, int timeout, CancellationToken ct)
    {
        if (uri.Scheme != "https") throw new NetworkCameraException(NetworkCameraState.Unsupported);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeout));
        using var tcp=new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync(uri.IdnHost,uri.Port,deadline.Token).ConfigureAwait(false);
        string? fingerprint=null;
        using var ssl=new SslStream(tcp.GetStream(),false,(_,cert,_,_)=>
        {
            if (cert is null) return false;
            fingerprint=cert.GetCertHashString(HashAlgorithmName.SHA256); return true;
        });
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost=uri.IdnHost, EnabledSslProtocols=System.Security.Authentication.SslProtocols.Tls12|System.Security.Authentication.SslProtocols.Tls13 },deadline.Token).ConfigureAwait(false);
        return fingerprint ?? throw new NetworkCameraException(NetworkCameraState.Certificate);
    }
    private static CancellationTokenSource Deadline(NetworkCameraProfile p,CancellationToken ct)
    { var c=CancellationTokenSource.CreateLinkedTokenSource(ct);c.CancelAfter(TimeSpan.FromSeconds(p.TimeoutSeconds));return c; }

    private static async Task ReadHttp(NetworkCameraProfile p,Uri uri,Func<SKBitmap,int,string,double,double?,bool> emit,CancellationToken ct)
    {
        using var handler=new HttpClientHandler { AllowAutoRedirect=false,UseProxy=false,UseCookies=false,AutomaticDecompression=DecompressionMethods.None,
            SslProtocols=System.Security.Authentication.SslProtocols.Tls12|System.Security.Authentication.SslProtocols.Tls13 };
        if (p.User.Length>0)
        {var credential=new NetworkCredential(p.User,p.Password);var cache=new CredentialCache();cache.Add(uri,"Basic",credential);cache.Add(uri,"Digest",credential);handler.Credentials=cache;}
        if (uri.Scheme=="https" && p.Pin.Length>0)
            handler.ServerCertificateCustomValidationCallback=(_,cert,_,errors)=>CertificateMatches(cert,p.Pin,errors);
        using var client=new HttpClient(handler) { Timeout=Timeout.InfiniteTimeSpan };
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var timer=Stopwatch.StartNew();
            using var deadline=Deadline(p,ct);
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);
            request.Headers.CacheControl=new CacheControlHeaderValue{NoCache=true,NoStore=true};
            request.Headers.Accept.ParseAdd("image/jpeg, multipart/x-mixed-replace");
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new NetworkCameraException(NetworkCameraState.Authentication);
            if (!response.IsSuccessStatusCode) throw new NetworkCameraException(NetworkCameraState.Error);
            using var input=await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            var media=response.Content.Headers.ContentType?.MediaType;
            if (string.Equals(media,"multipart/x-mixed-replace",StringComparison.OrdinalIgnoreCase))
            {
                // MJPEG streams are not finite HTTP bodies. Each image has its own deadline.
                string? boundary=response.Content.Headers.ContentType?.Parameters.FirstOrDefault(x=>string.Equals(x.Name,"boundary",StringComparison.OrdinalIgnoreCase))?.Value?.Trim('"');
                if (string.IsNullOrWhiteSpace(boundary)||boundary.Length>100||boundary.Any(c=>c<33||c>126)) throw new NetworkCameraException(NetworkCameraState.Unsupported);
                using var reader=new NetworkCameraReader(input);
                long last=long.MinValue;
                while(true)
                {
                    using var perFrame=Deadline(p,ct);timer.Restart();
                    var bytes=await reader.ReadMultipart(boundary,perFrame.Token).ConfigureAwait(false);
                    var now=Environment.TickCount64;
                    if(last!=long.MinValue && now-last<1000/p.Fps){CryptographicOperations.ZeroMemory(bytes);continue;}
                    last=now;
                    if(EmitJpeg(bytes,"MJPEG",timer.Elapsed.TotalMilliseconds,emit))return;
                }
            }
            if (media is not null && !string.Equals(media,"image/jpeg",StringComparison.OrdinalIgnoreCase) && !string.Equals(media,"image/jpg",StringComparison.OrdinalIgnoreCase) && !string.Equals(media,"application/octet-stream",StringComparison.OrdinalIgnoreCase))
                throw new NetworkCameraException(NetworkCameraState.Unsupported);
            if(response.Content.Headers.ContentLength is > MaxFrameBytes)throw new NetworkCameraException(NetworkCameraState.Unsupported);
            using var body=new NetworkCameraReader(input);
            var snapshot=await body.ReadBody(deadline.Token).ConfigureAwait(false);
            if(EmitJpeg(snapshot,"JPEG",timer.Elapsed.TotalMilliseconds,emit))return;
            await Task.Delay(Math.Max(1,1000/p.Fps-(int)timer.ElapsedMilliseconds),ct).ConfigureAwait(false);
        }
    }

    private static async Task ReadRtsp(NetworkCameraProfile p,Uri uri,Func<SKBitmap,int,string,double,double?,bool> emit,CancellationToken ct)
    {
        using var lease=NetworkFfmpegManager.AcquireRuntime();
        // No shell, no PATH resolution, no external files as output. Video only; TCP interleaved.
        var start=new ProcessStartInfo(Decoder){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(Decoder)!};
        // Avoid inherited proxy/debug/FFmpeg report settings.
        foreach(var key in start.Environment.Keys.Where(k=>k.Contains("proxy",StringComparison.OrdinalIgnoreCase)||k.Equals("FFREPORT",StringComparison.OrdinalIgnoreCase)||k.Equals("SSLKEYLOGFILE",StringComparison.OrdinalIgnoreCase)).ToArray())start.Environment.Remove(key);
        void Add(params string[] args){foreach(var arg in args)start.ArgumentList.Add(arg);}
        Add("-hide_banner","-loglevel","error","-nostdin","-nostats","-rtsp_transport",p.RtspUdp?"udp":"tcp","-allowed_media_types","video","-timeout",(p.TimeoutSeconds*1_000_000L).ToString(System.Globalization.CultureInfo.InvariantCulture),"-max_pixels",MaxPixels.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Add("-protocol_whitelist",p.RtspUdp?"rtsp,rtsps,tcp,tls,rtp,udp,pipe":"rtsp,rtsps,tcp,tls,rtp,pipe","-f","rtsp");
        if(p.RtspUdp)Add("-rtsp_flags","filter_src");
        if(uri.Scheme=="rtsps")
        {
            Add("-tls_verify","1","-verifyhost",uri.IdnHost);
            if(p.CaFile.Length>0)Add("-ca_file",p.CaFile);
        }
        // FFmpeg accepts RTSP Basic/Digest credentials in the input URI; visible to local process inspectors.
        var address=new UriBuilder(uri);
        if(p.User.Length>0){address.UserName=Uri.EscapeDataString(p.User);address.Password=Uri.EscapeDataString(p.Password);}
        Add("-i",address.Uri.AbsoluteUri,"-map","0:v:0","-an","-sn","-dn","-vf",$"select=isnan(prev_selected_t)+gte(t-prev_selected_t\\,1/{p.Fps})","-fps_mode","passthrough","-c:v","bmp","-pix_fmt","bgra","-f","image2pipe","pipe:1");
        using var process=Process.Start(start)??throw new NetworkCameraException(NetworkCameraState.DecoderMissing);
        void Kill(){try{if(!process.HasExited)process.Kill(entireProcessTree:true);}catch{}}
        using var registration=ct.Register(Kill);
        // Never store or expose FFmpeg's raw stderr (may contain URLs/credentials).
        var error=DrainErrors(process.StandardError);
        try
        {
            using var reader=new NetworkCameraReader(process.StandardOutput.BaseStream);
            while(true)
            {
                using var deadline=Deadline(p,ct);var timer=Stopwatch.StartNew();
                var bitmap=await reader.ReadBgra(deadline.Token).ConfigureAwait(false);
                if(emit(bitmap,checked(bitmap.Width*bitmap.Height*4),"FFmpeg / BGRA",timer.Elapsed.TotalMilliseconds,null))return;
            }
        }
        catch(EndOfStreamException)
        {
            Kill(); var state=await error.ConfigureAwait(false); throw new NetworkCameraException(state);
        }
        finally
        {
            Kill();
            try{await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);}catch{}
            try{await error.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);}catch{}
        }
    }
    private static async Task<NetworkCameraState> DrainErrors(StreamReader reader)
    {
        var state=NetworkCameraState.Error;
        // Fixed bounded buffer; do not accumulate arbitrary diagnostics in memory.
        char[] chunk=new char[4096];
        try
        {
            int n;while((n=await reader.ReadAsync(chunk).ConfigureAwait(false))>0)
            {
                var s=new string(chunk,0,n);
                if(s.Contains("certificate",StringComparison.OrdinalIgnoreCase)||s.Contains("TLS",StringComparison.OrdinalIgnoreCase))state=NetworkCameraState.Certificate;
                else if(s.Contains("401 Unauthorized",StringComparison.OrdinalIgnoreCase)||s.Contains("403 Forbidden",StringComparison.OrdinalIgnoreCase))state=NetworkCameraState.Authentication;
                else if(s.Contains("timed out",StringComparison.OrdinalIgnoreCase))state=NetworkCameraState.Timeout;
            }
        }
        catch{}finally{Array.Clear(chunk);}
        return state;
    }
}
