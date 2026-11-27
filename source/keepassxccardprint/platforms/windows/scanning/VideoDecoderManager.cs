using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace KeePassXCCardPrint.Services;

internal sealed record VideoDecoderCandidate(string Id,string Label,int Policy,int Adapter,bool Hardware,int Vendor=0);
internal enum VideoProbeStatus { NotTested, Working, Unsupported, CodecMissing, InitializationFailed, DecodeFailed, UnverifiedHardware, RuntimeMissing, SessionUnavailable }
internal sealed record VideoDecoderProbe(string Id,string Codec,bool Passed,int Pictures,double Milliseconds,string Detail,
    VideoProbeStatus Status=VideoProbeStatus.NotTested,int? CapabilityResult=null,bool At1080=false,int? HResult=null,int? Stage=null,double FirstPictureMs=0,double P95Ms=0,int Inputs=0);
internal sealed record VideoDecoderProgress(int Completed,int Total,VideoDecoderCandidate? Candidate,string? Codec,VideoDecoderProbe? Result);
internal sealed record VideoDecoderCache(string Fingerprint,VideoDecoderProbe[] Results);
[JsonSerializable(typeof(VideoDecoderCache))]
internal partial class VideoDecoderCacheJson : JsonSerializerContext { }

// One candidate per actual DXGI adapter, plus independent CPU backends.
// Direct vendor SDKs are independent of the Windows MFT inventory.
internal static class VideoDecoderManager
{
    private static readonly object Gate=new();
    private static VideoDecoderCache? _cache;
    private static string CacheFile=>Path.Combine(SettingsLocation.AppDataRoot,"scanner-decoders.json");
    internal static bool ValidSelection(string value)=>value is "auto" or "hardware" or "software" or "mf-cpu" or "libde265" ||
        value is not null&&(value.StartsWith("mf-gpu-",StringComparison.Ordinal)||value.StartsWith("nvdec-",StringComparison.Ordinal)||value.StartsWith("onevpl-",StringComparison.Ordinal)||value.StartsWith("amf-",StringComparison.Ordinal))&&value.Length<=80&&value.All(c=>char.IsAsciiLetterOrDigit(c)||c=='-');
    internal static VideoDecoderCandidate[] Candidates()
    {
        var result=new List<VideoDecoderCandidate>();
        foreach(var line in MediaFoundationVideoDecoder.AdapterInventory().Split('\n',StringSplitOptions.RemoveEmptyEntries))
        {
            var fields=line.Split('|',4);if(fields.Length!=4||!int.TryParse(fields[0],NumberStyles.None,CultureInfo.InvariantCulture,out int index)||index is <0 or >=32)continue;
            var vendor=fields[1].Split('-')[0].ToUpperInvariant();
            var direct=vendor switch {"10DE"=>(1,"nvdec","NVDEC"),"8086"=>(2,"onevpl","Quick Sync / oneVPL"),"1002"=>(3,"amf","AMF"),_=>(0,"","")};
            if(direct.Item1!=0)result.Add(new($"{direct.Item2}-{index}-{fields[1]}",fields[3]+" / "+direct.Item3,0,index,true,direct.Item1));
            result.Add(new($"mf-gpu-{index}-{fields[1]}",fields[3]+" / D3D11",1,index,true));
        }
        result.Add(new("mf-cpu","Media Foundation (CPU)",2,-1,false));
        result.Add(new("libde265","libde265 (CPU)",0,-1,false));
        return result.ToArray();
    }
    internal static bool Supports(VideoDecoderCandidate c,string codec)=>c.Id!="libde265"||codec!="H264";
    // UI reads a published immutable snapshot; never wait for a long-running probe.
    // An unknown result is not a failure. Before the stream format is known, a
    // decoder is offered if at least one supported profile is working or untested.
    internal static VideoDecoderCandidate[] Available(string? codec=null)
        => Available(Candidates(),Volatile.Read(ref _cache)?.Results??[],codec);
    internal static VideoDecoderCandidate[] Available(VideoDecoderCandidate[] candidates,VideoDecoderProbe[] probes,string? codec)
    {
        string[] formats=codec switch
        {
            "H264"=>["H264"],"H265"=>["H265"],"Main10"=>["Main10"],"HEVC"=>["H265","Main10"],
            "JPEG"=>[],_=>["H264","H265","Main10"]
        };
        return candidates.Where(c=>formats.Any(f=>Supports(c,f)&&
            !probes.Any(p=>p.Id==c.Id&&p.Codec==f&&!p.Passed&&p.Status!=VideoProbeStatus.NotTested))).ToArray();
    }
    private static string Fingerprint()=>string.Join("\n","raw-pixels-v1",typeof(VideoDecoderManager).Assembly.GetName().Version?.ToString(),Environment.OSVersion.VersionString,
        MediaFoundationVideoDecoder.AdapterInventory(),MediaFoundationVideoDecoder.Inventory(264),MediaFoundationVideoDecoder.Inventory(265));
    private static void Save(VideoDecoderCache cache)
    {
        string? temp=null;
        try{Directory.CreateDirectory(SettingsLocation.AppDataRoot);temp=CacheFile+"."+Guid.NewGuid().ToString("N")+".tmp";
            File.WriteAllBytes(temp,JsonSerializer.SerializeToUtf8Bytes(cache,VideoDecoderCacheJson.Default.VideoDecoderCache));File.Move(temp,CacheFile,true);}
        catch(IOException){}catch(UnauthorizedAccessException){}
        finally{if(temp is not null)try{File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    internal static void DeleteCache()
    {lock(Gate){_cache=null;if(File.Exists(CacheFile))File.Delete(CacheFile);}}
    // Must be called on a worker thread, before the camera starts delivering RTP.
    internal static VideoDecoderProbe[] ProbeAll(bool force,CancellationToken ct,VideoDecoderCandidate[]? candidates=null,IProgress<VideoDecoderProgress>? progress=null)
    {
        lock(Gate)
        {
            ct.ThrowIfCancellationRequested();string fingerprint=Fingerprint();
            if(!force&&_cache?.Fingerprint==fingerprint)return _cache.Results;
            if(!force)try
            {
                if(File.Exists(CacheFile)&&new FileInfo(CacheFile).Length<=256*1024)
                {
                    var cached=JsonSerializer.Deserialize(File.ReadAllBytes(CacheFile),VideoDecoderCacheJson.Default.VideoDecoderCache);
                    if(cached?.Fingerprint==fingerprint&&cached.Results is not null){_cache=cached;return cached.Results;}
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(JsonException){}
            var results=new List<VideoDecoderProbe>();
            var inventory=candidates??Candidates();int total=inventory.Length*3;
            foreach(var c in inventory)foreach(var codec in new[]{"H264","H265","Main10"})
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new(results.Count,total,c,codec,null));
                var result=!Supports(c,codec)?new VideoDecoderProbe(c.Id,codec,false,0,0,"Not supported by this backend",VideoProbeStatus.Unsupported):Probe(c,codec,ct);
                results.Add(result);progress?.Report(new(results.Count,total,c,codec,result));
            }
            var fresh=new VideoDecoderCache(fingerprint,results.ToArray());_cache=fresh;Save(fresh);return fresh.Results;
        }
    }
    private static VideoDecoderProbe Probe(VideoDecoderCandidate candidate,string codec,CancellationToken ct)
    {
        string actual=codec=="H264"?"H264":"H265";int profile=codec=="H264"?0:codec=="Main10"?2:1;
        var frames=codec=="H264"?DecoderProbeFixtures.H264:codec=="Main10"?DecoderProbeFixtures.Main10:DecoderProbeFixtures.Main8;
        int count=0;bool normal=false,inverted=false;double ms=0,firstPicture=0;var durations=new List<double>();var watch=Stopwatch.StartNew();
        int? cap=null;bool at1080=false;
        if(candidate.Hardware&&candidate.Vendor==0)
        {
            var capability=MediaFoundationVideoDecoder.Capability(candidate.Adapter,actual=="H264"?264:265,profile);
            cap=capability.Result;at1080=capability.At1080;
            if(cap==1)return new(candidate.Id,codec,false,0,0,"D3D11 driver does not advertise this profile/output format",VideoProbeStatus.Unsupported,cap,at1080);
        }
        DecoderBackend? decoder=null;
        try
        {
            decoder=new DecoderBackend(candidate);
            for(int i=0;i<frames.Length;i++)
            {
                ct.ThrowIfCancellationRequested();if(watch.Elapsed.TotalSeconds>12)return new(candidate.Id,codec,false,count,0,"Probe timeout",VideoProbeStatus.DecodeFailed,cap,at1080,decoder.HResult,decoder.Stage);
                var unit=Convert.FromBase64String(frames[i]);var sw=Stopwatch.StartNew();var image=decoder.Decode(actual,unit,i*1_000_000L,true,profile);double elapsed=sw.Elapsed.TotalMilliseconds;ms+=elapsed;durations.Add(elapsed);
                if(image is null)continue;
                try
                {
                    var bmp=image;if(bmp.Width!=DecoderProbeFixtures.Width||bmp.Height!=DecoderProbeFixtures.Height)return new(candidate.Id,codec,false,count,0,"Wrong dimensions",VideoProbeStatus.DecodeFailed,cap,at1080,decoder.HResult,decoder.Stage);
                    var left=bmp.GetPixel(DecoderProbeFixtures.Width/4,DecoderProbeFixtures.Height/2).Red;var right=bmp.GetPixel(DecoderProbeFixtures.Width*3/4,DecoderProbeFixtures.Height/2).Red;
                    normal|=left<50&&right>200;inverted|=left>200&&right<50;if(count==0)firstPicture=ms;count++;
                }
                finally{ScannerPixels.ClearAndDispose(image);}
            }
            bool passed=count>=2&&normal&&inverted;
            var sorted=durations.Order().ToArray();double p95=sorted.Length==0?0:sorted[Math.Max(0,(int)Math.Ceiling(sorted.Length*.95)-1)];
            return new(candidate.Id,codec,passed,count,count==0?0:ms/count,decoder.Format+(passed?"":" / invalid or incomplete probe output"),passed?VideoProbeStatus.Working:VideoProbeStatus.DecodeFailed,cap,at1080,decoder.HResult,decoder.Stage,firstPicture,p95,frames.Length);
        }
        catch(NetworkCameraException ex)
        {
            var status=candidate.Vendor!=0&&decoder?.HResult==VendorVideoDecoder.Unsupported?VideoProbeStatus.Unsupported:
                candidate.Vendor!=0&&decoder?.HResult==VendorVideoDecoder.Missing&&decoder?.Stage==2?VideoProbeStatus.SessionUnavailable:
                candidate.Vendor!=0&&(decoder?.HResult==VendorVideoDecoder.Missing||decoder?.Stage==1)?VideoProbeStatus.RuntimeMissing:
                decoder?.Stage==11?VideoProbeStatus.UnverifiedHardware:
                ex.State==NetworkCameraState.SystemCodecMissing?VideoProbeStatus.CodecMissing:
                ex.State==NetworkCameraState.VideoInitializationFailed?VideoProbeStatus.InitializationFailed:VideoProbeStatus.DecodeFailed;
            return new(candidate.Id,codec,false,count,0,ex.Detail,status,cap,at1080,decoder?.HResult,decoder?.Stage);
        }
        finally{decoder?.Dispose();}
    }
    internal static VideoDecoderCandidate[] Plan(string selection,bool fallback,string codec,int profile)
    {
        var all=Candidates().Where(c=>Supports(c,codec)).ToArray();
        string probeCodec=codec=="H264"?"H264":profile==2?"Main10":"H265";
        VideoDecoderProbe[] known;lock(Gate)known=_cache?.Results??[];
        return Rank(all,known,selection,fallback,probeCodec);
    }
    internal static VideoDecoderCandidate[] Rank(VideoDecoderCandidate[] all,VideoDecoderProbe[] known,string selection,bool fallback,string probeCodec)
    {
        VideoDecoderProbe? Result(VideoDecoderCandidate c)=>known.FirstOrDefault(p=>p.Id==c.Id&&p.Codec==probeCodec);
        bool Unavailable(VideoDecoderCandidate c)=>Result(c) is { } p&&(p.CapabilityResult==1||p.Status is VideoProbeStatus.Unsupported or VideoProbeStatus.RuntimeMissing or VideoProbeStatus.SessionUnavailable or VideoProbeStatus.CodecMissing);
        double Score(VideoDecoderCandidate c)
        {
            var p=Result(c);if(p is not {Passed:true}||!double.IsFinite(p.Milliseconds)||p.Milliseconds<=0)return double.MaxValue;
            // Penalize stalls as well as slow output. No hardware/vendor preference.
            return p.Milliseconds+Math.Max(0,p.P95Ms-p.Milliseconds)*.25+Math.Max(0,p.FirstPictureMs)*.05;
        }
        var ordered=all.Where(c=>!Unavailable(c)).OrderBy(c=>Result(c)?.Passed==true?0:Result(c) is null?1:2)
            .ThenBy(c=>Result(c) is {Passed:true,Inputs:>0} p&&p.Pictures*4<p.Inputs*3?1:0)
            .ThenBy(Score).ThenBy(c=>Result(c)?.FirstPictureMs??double.MaxValue).ToArray();
        if(selection=="auto")return ordered;
        if(selection=="hardware")return ordered.Where(c=>c.Hardware).ToArray();
        if(selection=="software")return ordered.Where(c=>!c.Hardware).ToArray();
        // Explicit selection is never silently replaced, including unavailable IDs.
        var primary=all.Where(c=>c.Id==selection).ToArray();
        return primary.Length==0||!fallback?primary:primary.Concat(ordered.Where(c=>c.Id!=selection)).ToArray();
    }
}

// Backend lifetime is bound to one decoding worker.
internal sealed class DecoderBackend : IDisposable
{
    private readonly MediaFoundationVideoDecoder? _mf;
    private readonly SoftwareHevcDecoder? _hevc;
    private readonly VendorVideoDecoder? _vendor;
    private readonly VideoDecoderCandidate _candidate;
    internal DecoderBackend(VideoDecoderCandidate candidate)
    {
        _candidate=candidate;if(candidate.Id=="libde265")_hevc=new();else if(candidate.Vendor!=0)_vendor=new(candidate.Vendor,candidate.Adapter);else _mf=new(candidate.Policy,candidate.Adapter);
    }
    internal string Format { get; private set; }="";
    internal int? HResult=>_vendor?.LastHResult??_mf?.LastHResult;
    internal int? Stage=>_vendor?.LastStage??_mf?.LastStage;
    internal SKBitmap? Decode(string codec,byte[] unit,long time,bool emit,int profile)
    {
        if(_hevc is not null){var result=_hevc.Decode(unit,time,emit);Format=_hevc.Format;return result;}
        if(_vendor is not null){var result=_vendor.Decode(codec,unit,time,emit,profile);Format=$"{_candidate.Label} / {codec} / {_vendor.Depth}-bit";return result;}
        var frame=_mf!.Decode(codec,unit,time,emit,profile);Format=$"{_candidate.Label} / {codec} / {_mf.Mode} / {_mf.Depth}-bit";return frame;
    }
    public void Dispose(){_hevc?.Dispose();_mf?.Dispose();_vendor?.Dispose();}
}
