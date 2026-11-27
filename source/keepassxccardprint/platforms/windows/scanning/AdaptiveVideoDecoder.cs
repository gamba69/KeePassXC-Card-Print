using SkiaSharp;

namespace KeePassXCCardPrint.Services;

// Session-local fallback, only at random-access AUs. Transport/TLS never changes.
internal sealed class AdaptiveVideoDecoder : IDisposable
{
    private readonly string _selection;
    private readonly bool _fallbackAllowed;
    private readonly Action<string>? _streamCodec;
    private VideoDecoderCandidate[] _plan=[];
    private DecoderBackend? _backend;
    private int _index,_profile,_missingOutputs,_recoveryAttempts;
    private bool _waitingForKey=true;
    private long _firstMissing;
    private string? _codec;
    private string _failure="";
    internal AdaptiveVideoDecoder(string selection="auto",bool fallback=true,Action<string>? streamCodec=null){_selection=selection;_fallbackAllowed=fallback;_streamCodec=streamCodec;}
    internal string Format { get; private set; }="";
    internal string LastFailure=>_failure;
    internal static (bool Key,int Profile) Inspect(byte[] unit,bool hevc)
    {
        bool key=false;int profile=0;
        foreach(var nal in AnnexBUnits.Split(unit))
        {
            int type=SharpNetworkCameraSource.NalType(nal,hevc);key|=SharpNetworkCameraSource.RandomAccess(type,hevc);
            if(hevc&&type==33&&nal.Length>=4){int p=nal.Span[3]&31;if(p is 1 or 2)profile=p;}
        }
        return (key,profile);
    }
    private void ResetBackend(){_backend?.Dispose();_backend=null;_waitingForKey=true;_missingOutputs=0;_firstMissing=0;}
    internal SKBitmap? Decode(string codec,byte[] unit,long time,bool emit)
    {
        if(_codec is not null&&!_codec.Equals(codec,StringComparison.OrdinalIgnoreCase))throw new NetworkCameraException(NetworkCameraState.Unsupported,"Video codec changed during RTSP session");
        bool hevc=codec.Equals("H265",StringComparison.OrdinalIgnoreCase);
        var (key,profile)=Inspect(unit,hevc);
        if(_codec is null||hevc&&profile!=0&&_profile!=profile)
        {
            _codec=codec;if(profile!=0)_profile=profile;
            _streamCodec?.Invoke(hevc?(_profile==2?"Main10":_profile==1?"H265":"HEVC"):"H264");
            ResetBackend();_plan=VideoDecoderManager.Plan(_selection,_fallbackAllowed,codec,_profile);_index=0;_recoveryAttempts=0;_failure="";
        }
        if(_plan.Length==0)throw new NetworkCameraException(NetworkCameraState.VideoInitializationFailed,"No decoder matches the selected policy / codec: "+_selection+" / "+codec);
        if(_waitingForKey&&!key)return null;
        _waitingForKey=false;
        while(_index<_plan.Length)
        {
            var candidate=_plan[_index];
            try
            {
                _backend??=new DecoderBackend(candidate);
                var result=_backend.Decode(codec,unit,time,emit,hevc?_profile:0);
                Format=_backend.Format+(_failure.Length==0?"":" · "+_failure);
                if(result is not null){_missingOutputs=0;_firstMissing=0;_recoveryAttempts=0;}
                else if(emit)
                {
                    long now=Environment.TickCount64;if(_missingOutputs++==0)_firstMissing=now;
                    if(_missingOutputs>=24||now-_firstMissing>=2500)throw new NetworkCameraException(NetworkCameraState.VideoDecodeFailed,"No picture output");
                }
                return result;
            }
            catch(NetworkCameraException ex) when(ex.State is NetworkCameraState.SystemCodecMissing or NetworkCameraState.VideoInitializationFailed or NetworkCameraState.VideoDecodeFailed)
            {
                var detail=candidate.Label+": "+ex.Detail;
                if(!_failure.EndsWith(detail,StringComparison.Ordinal))_failure=(_failure.Length==0?"":_failure+" · ")+detail;
                if(_failure.Length>2048)_failure=_failure[^2048..];
                ResetBackend();
                // A damaged HEVC access unit must not poison the next GOP. Allow
                // two bounded recovery attempts, then report rather than loop forever.
                if(candidate.Id=="libde265"&&ex.State==NetworkCameraState.VideoDecodeFailed&&_recoveryAttempts++<2)return null;
                _index++;_recoveryAttempts=0;
                if(_index>=_plan.Length)throw new NetworkCameraException(ex.State,_failure);
                if(!key)return null;
                _waitingForKey=false;
            }
        }
        return null;
    }
    public void Dispose()=>ResetBackend();
}
