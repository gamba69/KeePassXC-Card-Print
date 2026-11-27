using System.Runtime.InteropServices;
using SkiaSharp;

namespace KeePassXCCardPrint.Services;

// Direct driver APIs, independent of Media Foundation and FFmpeg. The native
// bridge owns each SDK session and reads back a verified hardware-decoded frame.
internal sealed partial class VendorVideoDecoder(int kind,int adapter) : IDisposable
{
    private IntPtr _handle;
    private int _codec,_profile;
    internal int LastHResult { get; private set; }
    internal int LastStage { get; private set; }
    internal int DriverCode { get; private set; }
    internal int Depth { get; private set; }
    internal string Name=>kind==1?"NVDEC":kind==2?"oneVPL / Quick Sync":"AMF";
    internal string Detail=>$"{Name} / {LastStage switch{1=>"runtime",2=>"adapter / session",3=>"profile / initialization",4=>"submit",5=>"output",6=>"pixel conversion",_=>"bridge"}} / SDK={DriverCode} / HRESULT=0x{LastHResult:X8}";
    internal const int Unsupported=unchecked((int)0x80070032),Missing=unchecked((int)0x8007007E);
    [LibraryImport("KpcVendorVideo.dll",EntryPoint="KpcVendorOpen")]
    private static partial int Open(int kind,int adapter,int codec,int profile,uint width,uint height,out IntPtr handle,out int stage,out int code);
    [LibraryImport("KpcVendorVideo.dll",EntryPoint="KpcVendorPush")]
    private static unsafe partial int Push(IntPtr handle,byte* bytes,uint size,long time,out IntPtr pixels,out uint width,out uint height,out int depth,out int stage,out int code);
    [LibraryImport("KpcVendorVideo.dll",EntryPoint="KpcVendorClose")]
    private static partial void Close(IntPtr handle);
    internal unsafe SKBitmap? Decode(string codec,byte[] unit,long time,bool emit,int profile)
    {
        int id=codec=="H265"?265:264;
        if(_handle==IntPtr.Zero||id!=_codec||profile!=_profile)
        {
            Dispose();uint width=0,height=0;
            if(kind==3)
            {
                if(!VendorStreamSize.TryRead(unit,id==265,out width,out height))
                {LastStage=3;LastHResult=unchecked((int)0x8007000D);throw new NetworkCameraException(NetworkCameraState.VideoInitializationFailed,Name+" / no valid SPS dimensions");}
            }
            try{LastHResult=Open(kind,adapter,id,profile,width,height,out _handle,out int stage,out int code);LastStage=stage;DriverCode=code;}
            catch(Exception ex) when(ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException){LastStage=1;LastHResult=ex.HResult;throw new NetworkCameraException(NetworkCameraState.VideoInitializationFailed,Detail);}
            if(LastHResult<0||_handle==IntPtr.Zero)throw new NetworkCameraException(NetworkCameraState.VideoInitializationFailed,Detail);
            _codec=id;_profile=profile;
        }
        IntPtr pixels;uint w,h;int depth,stageOut,driverCode;
        fixed(byte* p=unit)LastHResult=Push(_handle,p,(uint)unit.Length,time,out pixels,out w,out h,out depth,out stageOut,out driverCode);
        LastStage=stageOut;DriverCode=driverCode;Depth=depth;
        if(LastHResult<0)throw new NetworkCameraException(NetworkCameraState.VideoDecodeFailed,Detail);
        if(LastHResult!=0||!emit||pixels==IntPtr.Zero)return null;
        if(w==0||h==0||(long)w*h>32_000_000)throw new NetworkCameraException(NetworkCameraState.VideoDecodeFailed,Name+" / invalid dimensions");
        return ScannerPixels.CopyBgra(pixels,(int)w,(int)h);
    }
    internal static void VerifyPackagedBridge()
    {
        foreach(var (library,exports) in new (string,string[])[]{
            ("KpcVendorVideo.dll",["KpcVendorOpen","KpcVendorPush","KpcVendorClose"]),
            ("libvpl.dll",["MFXLoad","MFXCreateSession","MFXVideoDECODE_DecodeFrameAsync","MFXInit"])})
        {
            IntPtr module=NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory,library));
            try{foreach(string symbol in exports)if(!NativeLibrary.TryGetExport(module,symbol,out _))throw new InvalidOperationException(library+" missing "+symbol);}
            finally{NativeLibrary.Free(module);}
        }
        int result=Open(0,-1,264,0,0,0,out var invalid,out _,out _);
        if(invalid!=IntPtr.Zero){Close(invalid);throw new InvalidOperationException("Vendor decoder accepted an invalid adapter");}
        if(result>=0)throw new InvalidOperationException("Vendor decoder accepted an invalid backend");
    }
    public void Dispose(){if(_handle!=IntPtr.Zero){Close(_handle);_handle=IntPtr.Zero;}}
}

// AMF needs initial dimensions; read the SPS instead of assuming 1080p. This
// bounded parser accepts ordinary 4:2:0 H.264 and HEVC, including cropped frames.
internal static class VendorStreamSize
{
    private sealed class Bits(byte[] rbsp)
    {
        private int _offset;
        internal uint Read(int count){if(count<0||count>32||_offset>rbsp.Length*8-count)throw new InvalidDataException();uint n=0;for(int i=0;i<count;i++){n=(n<<1)|(uint)((rbsp[_offset/8]>>(7-_offset%8))&1);_offset++;}return n;}
        internal void Skip(int count){while(count>0){int n=Math.Min(count,32);Read(n);count-=n;}}
        internal uint Ue(){int n=0;while(Read(1)==0){if(++n>30)throw new InvalidDataException();}return ((1u<<n)-1)+Read(n);}
        internal int Se(){uint u=Ue();return (u&1)!=0?(int)((u+1)/2):-(int)(u/2);}
    }
    internal static bool TryRead(byte[] unit,bool hevc,out uint width,out uint height)
    {
        width=height=0;
        try
        {
            foreach(var nal in AnnexBUnits.Split(unit))
            {
                if(SharpNetworkCameraSource.NalType(nal,hevc)!=(hevc?33:7))continue;
                var src=nal.Span;if(src.Length>(64*1024)||src.Length<(hevc?3:2))continue;
                var bytes=new List<byte>();int zeros=0;
                foreach(byte b in src[(hevc?2:1)..]){if(zeros>=2&&b==3){zeros=0;continue;}bytes.Add(b);zeros=b==0?zeros+1:0;}
                var r=new Bits(bytes.ToArray());
                uint w,h;
                if(hevc)
                {
                    r.Skip(4);int layers=(int)r.Read(3);if(layers>6)return false;r.Skip(1+96);
                    var subProfile=new bool[layers];var subLevel=new bool[layers];for(int i=0;i<layers;i++){subProfile[i]=r.Read(1)!=0;subLevel[i]=r.Read(1)!=0;}
                    if(layers>0)r.Skip((8-layers)*2);for(int i=0;i<layers;i++){if(subProfile[i])r.Skip(88);if(subLevel[i])r.Skip(8);}
                    r.Ue();if(r.Ue()!=1)return false;w=r.Ue();h=r.Ue();
                    if(!Valid(w,h))return false;
                    if(r.Read(1)!=0){uint left=r.Ue(),right=r.Ue(),top=r.Ue(),bottom=r.Ue();ulong x=2*((ulong)left+right),y=2*((ulong)top+bottom);if(x>=w||y>=h)return false;w-=(uint)x;h-=(uint)y;}
                }
                else
                {
                    uint profile=r.Read(8);r.Skip(16);r.Ue();
                    if(profile is 100 or 110 or 122 or 244 or 44 or 83 or 86 or 118 or 128 or 138 or 139 or 134 or 135 or 144)
                    {
                        if(r.Ue()!=1)return false; if(r.Ue()!=0||r.Ue()!=0)return false;r.Skip(1);
                        if(r.Read(1)!=0)for(int i=0;i<8;i++)if(r.Read(1)!=0){int last=8,next=8;for(int k=0;k<(i<6?16:64);k++){if(next!=0)next=(last+r.Se()+256)%256;if(next!=0)last=next;}}
                    }
                    r.Ue();uint poc=r.Ue();if(poc==0)r.Ue();else if(poc==1){r.Skip(1);r.Se();r.Se();uint count=r.Ue();if(count>255)return false;for(int i=0;i<count;i++)r.Se();}else if(poc!=2)return false;
                    r.Ue();r.Skip(1);ulong ww=((ulong)r.Ue()+1)*16,hh=((ulong)r.Ue()+1)*16;bool frame=r.Read(1)!=0;if(!frame){hh*=2;r.Skip(1);}r.Skip(1);
                    if(ww>8192||hh>8192||!Valid((uint)ww,(uint)hh))return false;
                    if(r.Read(1)!=0){uint left=r.Ue(),right=r.Ue(),top=r.Ue(),bottom=r.Ue();ulong x=2*((ulong)left+right),y=(frame?2u:4u)*((ulong)top+bottom);if(x>=ww||y>=hh)return false;ww-=x;hh-=y;}
                    w=(uint)ww;h=(uint)hh;
                }
                if(!Valid(w,h))return false;width=w;height=h;return true;
            }
        }
        catch(Exception e) when(e is InvalidDataException or NetworkCameraException or OverflowException){return false;}
        return false;
    }
    private static bool Valid(uint w,uint h)=>w>0&&h>0&&w<=8192&&h<=8192&&(ulong)w*h<=32_000_000&&(w&1)==0&&(h&1)==0;
}
