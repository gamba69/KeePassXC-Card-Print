using System.Runtime.InteropServices;
using SkiaSharp;

namespace KeePassXCCardPrint.Services;

// Windows-specific Media Foundation bridge. Keep native entry points out of shared sources.
internal sealed partial class MediaFoundationVideoDecoder : IDisposable
{
    private IntPtr _handle;
    private readonly int _policy,_adapter;
    internal MediaFoundationVideoDecoder(int policy=0,int adapter=-1){_policy=policy;_adapter=adapter;}
    private int _codec,_profile,_mode;
    internal int LastStage { get; private set; }
    internal int Depth { get; private set; }
    internal string Mode => _mode==3?"MF hardware / D3D11":_mode==2?"MF hardware MFT / CPU output":_mode==1?"MF async":"MF sync";
    internal string Detail => $"{Mode} / {StageName(LastStage)} / HRESULT=0x{LastHResult:X8}";
    private static string StageName(int stage)=>stage switch {1=>"MF startup",2=>"enumerate",3=>"input / profile",4=>"output type",5=>"start streaming",6=>"output",7=>"pixel conversion",8=>"input",9=>"D3D11 device / awareness",11=>"decoder surface verification",_=>"bridge"};
    internal int LastHResult { get; private set; }
    internal const int CodecNotFound = unchecked((int)0xC00D5212);
    [LibraryImport("KpcVideoBridge.dll",EntryPoint="KpcVideoOpen")]
    private static partial int Open(int codec,uint profile,int policy,int adapter,out IntPtr handle,out int stage,out int mode);
    [LibraryImport("KpcVideoBridge.dll",EntryPoint="KpcVideoPush")]
    private static unsafe partial int Push(IntPtr handle,byte* data,uint length,long time,out IntPtr pixels,out uint width,out uint height,out int stage,out int depth,out int mode);
    [LibraryImport("KpcVideoBridge.dll",EntryPoint="KpcVideoClose")]
    private static partial void Close(IntPtr handle);
    [LibraryImport("KpcVideoBridge.dll",EntryPoint="KpcVideoInventory")]
    private static unsafe partial int Enumerate(int codec,char* text,uint capacity);
    [LibraryImport("KpcVideoBridge.dll",EntryPoint="KpcVideoAdapters")]
    private static unsafe partial int ReadAdapters(char* text,uint capacity);
    [LibraryImport("KpcVideoBridge.dll",EntryPoint="KpcVideoCapability")]
    private static partial int ReadCapability(int adapter,int codec,uint profile,out uint at1080);
    internal static (int Result,bool At1080) Capability(int adapter,int codec,int profile)
    {
        try{int hr=ReadCapability(adapter,codec,(uint)profile,out uint at1080);return (hr,at1080!=0);}
        catch(Exception e) when(e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException){return (e.HResult,false);}
    }
    internal static unsafe string AdapterInventory()
    {
        try{var buffer=new char[8192];int hr;fixed(char* p=buffer)hr=ReadAdapters(p,(uint)buffer.Length);
            int end=Array.IndexOf(buffer,'\0');return hr<0?"":new string(buffer,0,end<0?buffer.Length:end);}
        catch(Exception e) when(e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException){return "";}
    }
    internal static unsafe string Inventory(int codec)
    {
        try{var buffer=new char[8192];int hr;fixed(char* p=buffer)hr=Enumerate(codec,p,(uint)buffer.Length);
            int end=Array.IndexOf(buffer,'\0');string list=new(buffer,0,end<0?buffer.Length:end);
            return list.Length>0?list+(hr<0?$" / HRESULT=0x{hr:X8}":""):hr<0?$"HRESULT=0x{hr:X8}":"—";}
        catch(Exception e) when(e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException){return $"HRESULT=0x{e.HResult:X8}";}
    }
    internal bool Initialize(string codec,int profile=0)
    {
        int id=codec.Equals("H265",StringComparison.OrdinalIgnoreCase)?265:264;
        if(_handle!=IntPtr.Zero&&_codec==id&&_profile==profile)return true;
        Dispose();
        try
        {
            LastHResult=Open(id,(uint)profile,_policy,_adapter,out _handle,out int stage,out _mode);LastStage=stage;
            if(LastHResult<0||_handle==IntPtr.Zero)return false;
            _codec=id;_profile=profile;return true;
        }
        catch(DllNotFoundException e){LastHResult=e.HResult;return false;}
        catch(EntryPointNotFoundException e){LastHResult=e.HResult;return false;}
        catch(BadImageFormatException e){LastHResult=e.HResult;return false;}
    }
    internal unsafe SKBitmap? Decode(string codec,byte[] unit,long time,bool emit,int profile=0)
    {
        if(!Initialize(codec,profile))throw new NetworkCameraException(LastHResult==CodecNotFound?NetworkCameraState.SystemCodecMissing:NetworkCameraState.VideoInitializationFailed,Detail);
        int hr,stage,depth;IntPtr pixels;uint width,height;
        fixed(byte* p=unit)hr=Push(_handle,p,(uint)unit.Length,time,out pixels,out width,out height,out stage,out depth,out _mode);
        LastHResult=hr;LastStage=stage;Depth=depth;
        if(hr<0)throw new NetworkCameraException(NetworkCameraState.VideoDecodeFailed,Detail);
        if(hr!=0||!emit||pixels==IntPtr.Zero)return null;
        if(width==0||height==0||(long)width*height>32_000_000)throw new NetworkCameraException(NetworkCameraState.Unsupported);
        return ScannerPixels.CopyBgra(pixels,(int)width,(int)height);
    }
    public void Dispose(){if(_handle!=IntPtr.Zero){Close(_handle);_handle=IntPtr.Zero;}}
}
