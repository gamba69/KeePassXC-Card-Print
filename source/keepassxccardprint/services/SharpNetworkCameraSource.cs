using SkiaSharp;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SharpRTSPClient;

namespace KeePassXCCardPrint.Services;

// SharpRTSP handles RTSP/RTP/SDP and packet reassembly. Keep the decoder on a
// single MTA worker; never drop compressed interframes to implement FPS limiting.
internal static class SharpNetworkCameraSource
{
    private sealed record Unit(byte[] Bytes,string Codec,uint Timestamp);
    internal static bool ValidateTls(Uri uri, NetworkCameraProfile p, X509Certificate? remote, SslPolicyErrors errors)
    {
        if(remote is null)return false;
        using var cert=new X509Certificate2(remote);
        // Some RTSP TLS transports use the resolved IP as TargetHost. Verify the
        // configured identity ourselves, never accept an arbitrary chain/name.
        if(!cert.MatchesHostname(uri.IdnHost,allowWildcards:true,allowCommonName:false))return false;
        if(p.Pin.Length>0)return NetworkCameraSource.CertificateMatches(cert,p.Pin,errors);
        if((errors & SslPolicyErrors.RemoteCertificateNotAvailable)!=0)return false;
        if(p.CaFile.Length==0)return (errors & SslPolicyErrors.RemoteCertificateChainErrors)==0;
        var anchors=new X509Certificate2Collection();
        try
        {
            anchors.ImportFromPemFile(p.CaFile);
            using var chain=new X509Chain();
            chain.ChainPolicy.TrustMode=X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(anchors);
            chain.ChainPolicy.RevocationMode=X509RevocationMode.NoCheck; // offline camera trust anchor
            chain.ChainPolicy.DisableCertificateDownloads=true;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            return chain.Build(cert);
        }
        finally{foreach(var anchor in anchors)anchor.Dispose();}
    }
    internal static Task Run(NetworkCameraProfile p,Uri uri,Func<SKBitmap,int,string,double,double?,bool> emit,CancellationToken ct,Action<string>? streamCodec=null)
        =>Task.Run(()=>Read(p,uri,emit,ct,streamCodec),ct);

    private static void Read(NetworkCameraProfile p,Uri uri,Func<SKBitmap,int,string,double,double?,bool> emit,CancellationToken ct,Action<string>? streamCodec)
    {
        // The library accepts a bounded connect/read/response timeout; its callbacks
        // run on another thread and own their original memory until callback return.
        using var queue=new BlockingCollection<Unit>(12);
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
        var limit=TimeSpan.FromSeconds(p.TimeoutSeconds);
        using var client=new RTSPClient{Logger=NullLog.Instance,MaxTracks=1,ConnectTimeout=limit,ResponseTimeout=limit,ReceiveTimeout=limit};
        int failure=(int)NetworkCameraState.Error, tlsVerified=0, tlsRejected=0, stopping=0;
        byte[][] headers=[];
        var gate=new object();
        void Fail(NetworkCameraState state)
        {
            if(Volatile.Read(ref stopping)!=0)return;
            Interlocked.Exchange(ref failure,(int)state);
            try{stop.Cancel();}catch(ObjectDisposedException){}
        }
        client.AcceptTrack=t=>t.Kind==TrackKind.Video && t.AcceptedSoFar==0 && IsSupported(t.Codec);
        client.NewTrack+=(_,e)=>
        {
            lock(gate)
            {
                foreach(var h in headers)CryptographicOperations.ZeroMemory(h);
                headers=e.StreamConfigurationData switch
                {
                    H264StreamConfigurationData h=>CopyHeaders(h.SPS,h.PPS),
                    H265StreamConfigurationData h=>CopyHeaders(h.VPS,h.SPS,h.PPS),
                    _=>[]
                };
            }
        };
        client.Stopped+=(_,e)=>Fail(Volatile.Read(ref tlsRejected)!=0?NetworkCameraState.Certificate:e.Reason switch
        {
            StoppedReason.Unauthorized=>NetworkCameraState.Authentication,
            StoppedReason.UnsupportedMedia=>NetworkCameraState.Unsupported,
            StoppedReason.EncryptionUnavailable=>NetworkCameraState.Certificate,
            StoppedReason.ConnectTimeout or StoppedReason.ResponseTimeout or StoppedReason.ReceiveTimeout=>NetworkCameraState.Timeout,
            _=>NetworkCameraState.Error
        });
        client.ReceivedData+=(_,e)=>
        {
            if(e.Kind!=TrackKind.Video||stop.IsCancellationRequested||Volatile.Read(ref stopping)!=0)return;
            if(uri.Scheme=="rtsps"&&Volatile.Read(ref tlsVerified)==0){Fail(NetworkCameraState.Certificate);return;}
            byte[]? bytes=null;
            try
            {
                var segments=e.Data.Data.ToArray();
                lock(gate)
                {
                    if(!IsJpeg(e.Codec))UpdateHeaders(e.Codec,segments,ref headers);
                    bytes=Assemble(e.Codec,segments,headers);
                }
                if(bytes.Length==0)return;
                // A queue overflow must reset the whole predictive stream, rather than
                // decode corrupt video after dropping arbitrary reference frames.
                if(!queue.TryAdd(new Unit(bytes,e.Codec,e.Data.RtpTimestamp))) {Fail(NetworkCameraState.Error);return;}
                bytes=null;
            }
            catch(NetworkCameraException ex){Fail(ex.State);}
            catch{Fail(NetworkCameraState.Unsupported);}
            finally{if(bytes is not null)CryptographicOperations.ZeroMemory(bytes);}
        };
        RemoteCertificateValidationCallback validate=(_,cert,_,errors)=>
        {
            bool valid=false;
            try{valid=ValidateTls(uri,p,cert,errors);}catch{}
            Volatile.Write(ref tlsVerified,valid?1:0);Volatile.Write(ref tlsRejected,valid?0:1);
            return valid;
        };
        try
        {
            ct.ThrowIfCancellationRequested();
            if((p.Decoder is "auto" or "hardware" or "software")||p.DecoderFallback)VideoDecoderManager.ProbeAll(false,ct);
            // Uri does not know the RTSPS default port until the RTSP library
            // registers its parser. Normalize it explicitly before connecting.
            var address=uri.Port<0?new UriBuilder(uri){Port=uri.Scheme=="rtsps"?322:554}.Uri:uri;
            try{client.Connect(address,p.RtspUdp?RTPTransport.UDP:RTPTransport.TCP,p.User,p.Password,userCertificateSelectionCallback:validate,autoReconnect:false);}
            catch{throw new NetworkCameraException(Volatile.Read(ref tlsRejected)!=0?NetworkCameraState.Certificate:NetworkCameraState.Error);}
            using var decoder=new AdaptiveVideoDecoder(p.Decoder,p.DecoderFallback,streamCodec);
            bool jpegReported=false;
            long last=-1;uint? origin=null;
            var watch=Stopwatch.StartNew();long deadline=watch.ElapsedMilliseconds+p.TimeoutSeconds*1000L;
            while(true)
            {
                if(stop.IsCancellationRequested)throw new OperationCanceledException(stop.Token);
                if(watch.ElapsedMilliseconds>=deadline)throw new NetworkCameraException(NetworkCameraState.Timeout,decoder.LastFailure);
                int remaining=(int)Math.Clamp(deadline-watch.ElapsedMilliseconds,1,60_000);
                if(!queue.TryTake(out var unit,remaining,stop.Token))throw new NetworkCameraException(NetworkCameraState.Timeout,decoder.LastFailure);
                try
                {
                    var now=watch.ElapsedMilliseconds;bool due=last<0||now-last>=1000/p.Fps;
                    SKBitmap? image;var decodeWatch=Stopwatch.StartNew();
                    if(IsJpeg(unit.Codec))
                    {
                        if(!jpegReported){jpegReported=true;streamCodec?.Invoke("JPEG");}
                        if(!due)continue;
                        image=NetworkCameraSource.DecodeJpeg(unit.Bytes);
                    }
                    else
                    {
                        origin??=unit.Timestamp;
                        long time=(long)unchecked((int)(unit.Timestamp-origin.Value))*10_000_000L/90_000;
                        image=decoder.Decode(unit.Codec,unit.Bytes,time,due);
                    }
                    if(image is null)continue;
                    double decodeMs=decodeWatch.Elapsed.TotalMilliseconds;
                    long complete=watch.ElapsedMilliseconds;
                    double elapsed=last<0?complete:complete-last;
                    last=complete;deadline=complete+p.TimeoutSeconds*1000L;
                    if(emit(image,IsJpeg(unit.Codec)?unit.Bytes.Length:checked(image.Width*image.Height*4),"SharpRTSP / "+(IsJpeg(unit.Codec)?unit.Codec:decoder.Format),elapsed,decodeMs))return;
                }
                finally{CryptographicOperations.ZeroMemory(unit.Bytes);}
                if(watch.ElapsedMilliseconds>=deadline)throw new NetworkCameraException(NetworkCameraState.Timeout,decoder.LastFailure);
            }
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested)
        {throw new NetworkCameraException((NetworkCameraState)Volatile.Read(ref failure));}
        finally
        {
            Volatile.Write(ref stopping,1);
            try{client.Stop();}catch{}
            stop.Cancel();
            lock(gate){foreach(var h in headers)CryptographicOperations.ZeroMemory(h);headers=[];}
            while(queue.TryTake(out var unit))CryptographicOperations.ZeroMemory(unit.Bytes);
        }
    }
    private static bool IsJpeg(string codec)=>codec.Equals("JPEG",StringComparison.OrdinalIgnoreCase)||codec.Equals("MJPEG",StringComparison.OrdinalIgnoreCase);
    private static bool IsSupported(string codec)=>IsJpeg(codec)||codec.Equals("H264",StringComparison.OrdinalIgnoreCase)||codec.Equals("H265",StringComparison.OrdinalIgnoreCase);
    private static byte[][] CopyHeaders(params byte[]?[] values)=>values.Where(v=>v is {Length:>0 and <=65536}).Select(v=>v!.ToArray()).ToArray();
    // SharpRTSP 1.11.1 produces Annex B segments; SDP parameters are raw NALs.
    // Normalize exactly once, before inspecting the NAL header or constructing an AU.
    internal static ReadOnlyMemory<byte> StripStartCode(ReadOnlyMemory<byte> segment)
    {
        var span=segment.Span;int at=0;
        while(at<span.Length&&span[at]==0)at++;
        return at>=2&&at<span.Length&&span[at]==1?segment[(at+1)..]:segment;
    }
    internal static int NalType(ReadOnlyMemory<byte> segment,bool hevc)
    {
        var n=StripStartCode(segment).Span;
        if(n.Length<(hevc?2:1)||(n[0]&128)!=0||hevc&&(n[1]&7)==0)return -1;
        return hevc?(n[0]>>1)&63:n[0]&31;
    }
    internal static bool Parameter(int type,bool hevc)=>hevc?type is >=32 and <=34:type is 7 or 8;
    internal static bool RandomAccess(int type,bool hevc)=>hevc?type is >=16 and <=21:type==5;
    private static void UpdateHeaders(string codec,ReadOnlyMemory<byte>[] segments,ref byte[][] headers)
    {
        bool hevc=codec.Equals("H265",StringComparison.OrdinalIgnoreCase);
        var incoming=segments.Where(n=>Parameter(NalType(n,hevc),hevc)&&n.Length<=65540).Select(StripStartCode).ToArray();
        if(incoming.Length==0)return;
        // In-band parameters override stale SDP for the same NAL type. Preserve
        // multiple parameter sets of that type delivered in a single AU.
        var types=incoming.Select(n=>NalType(n,hevc)).ToHashSet();
        var keep=new List<byte[]>();
        foreach(var h in headers){if(types.Contains(NalType(h,hevc)))CryptographicOperations.ZeroMemory(h);else keep.Add(h);}
        foreach(var n in incoming.Take(32))if(!keep.Any(h=>h.AsSpan().SequenceEqual(n.Span)))keep.Add(n.ToArray());
        headers=keep.ToArray();
    }
    internal static byte[] Assemble(string codec,ReadOnlyMemory<byte>[] segments,byte[][] headers)
    {
        bool jpeg=IsJpeg(codec),hevc=codec.Equals("H265",StringComparison.OrdinalIgnoreCase);
        var units=segments.Where(n=>!n.IsEmpty).Select(n=>jpeg?n:StripStartCode(n)).ToArray();
        if(!jpeg&&units.Any(n=>NalType(n,hevc)<0))throw new NetworkCameraException(NetworkCameraState.VideoDecodeFailed,"Invalid RTP NAL header");
        bool key=!jpeg&&units.Any(n=>RandomAccess(NalType(n,hevc),hevc));
        var present=units.Select(n=>NalType(n,hevc)).ToHashSet();
        ReadOnlyMemory<byte>[] prefix=key?headers.Select(h=>StripStartCode(h)).Where(h=>Parameter(NalType(h,hevc),hevc)&&!present.Contains(NalType(h,hevc))).ToArray():[];
        long size=units.Sum(n=>(long)n.Length+(jpeg?0:4))+prefix.Sum(n=>(long)n.Length+4);
        if(size<=0||size>NetworkCameraSource.MaxFrameBytes)throw new NetworkCameraException(NetworkCameraState.Unsupported);
        var data=new byte[(int)size];int at=0;
        void Add(ReadOnlySpan<byte> n){if(!jpeg){data[at+3]=1;at+=4;}n.CopyTo(data.AsSpan(at));at+=n.Length;}
        // Preserve AUD at the front; order the parameter sets before the picture.
        int first=0;if(key&&units.Length>0&&NalType(units[0],hevc)==(hevc?35:9)){Add(units[0].Span);first=1;}
        if(key)
        {
            foreach(var h in prefix.Concat(units.Where(n=>Parameter(NalType(n,hevc),hevc))).OrderBy(n=>NalType(n,hevc)))Add(h.Span);
        }
        for(int i=first;i<units.Length;i++)if(!key||!Parameter(NalType(units[i],hevc),hevc))Add(units[i].Span);
        return data;
    }
}
