using System.Runtime.InteropServices;
using SkiaSharp;

namespace KeePassXCCardPrint.Services;

// libde265 is a separately replaceable LGPL DLL. All calls are confined to the
// same MTA worker as the RTSP session. No file, process or network is involved.
internal sealed partial class SoftwareHevcDecoder : IDisposable
{
    private const string Library="libde265.dll";
    private IntPtr _context;
    [StructLayout(LayoutKind.Sequential)]
    private struct Limits { public byte Version; public uint Pixels,NalBytes,SeiMessages; }
    [LibraryImport(Library,EntryPoint="de265_new_decoder")] private static partial IntPtr New();
    [LibraryImport(Library,EntryPoint="de265_free_decoder")] private static partial int Free(IntPtr ctx);
    [LibraryImport(Library,EntryPoint="de265_get_version_number")] private static partial uint Version();
    [LibraryImport(Library,EntryPoint="de265_set_security_limits")] private static partial void SetLimits(IntPtr ctx,in Limits limits);
    [LibraryImport(Library,EntryPoint="de265_set_parameter_bool")] private static partial void SetBool(IntPtr ctx,int parameter,int value);
    [LibraryImport(Library,EntryPoint="de265_start_worker_threads")] private static partial int StartThreads(IntPtr ctx,int count);
    [LibraryImport(Library,EntryPoint="de265_push_NAL")] private static unsafe partial int Push(IntPtr ctx,byte* bytes,int length,long pts,IntPtr user);
    [LibraryImport(Library,EntryPoint="de265_push_end_of_frame")] private static partial void EndFrame(IntPtr ctx);
    [LibraryImport(Library,EntryPoint="de265_decode")] private static partial int DecodeNative(IntPtr ctx,out int more);
    [LibraryImport(Library,EntryPoint="de265_isOK")] private static partial int IsOk(int code);
    [LibraryImport(Library,EntryPoint="de265_peek_next_picture")] private static partial IntPtr Peek(IntPtr ctx);
    [LibraryImport(Library,EntryPoint="de265_release_next_picture")] private static partial void Release(IntPtr ctx);
    [LibraryImport(Library,EntryPoint="de265_get_chroma_format")] private static partial int Chroma(IntPtr picture);
    [LibraryImport(Library,EntryPoint="de265_get_image_width")] private static partial int Width(IntPtr picture,int plane);
    [LibraryImport(Library,EntryPoint="de265_get_image_height")] private static partial int Height(IntPtr picture,int plane);
    [LibraryImport(Library,EntryPoint="de265_get_bits_per_pixel")] private static partial int Bits(IntPtr picture,int plane);
    [LibraryImport(Library,EntryPoint="de265_get_image_plane")] private static partial IntPtr Plane(IntPtr picture,int plane,out int stride);
    [LibraryImport(Library,EntryPoint="de265_get_image_full_range_flag")] private static partial int FullRange(IntPtr picture);
    [LibraryImport(Library,EntryPoint="de265_get_image_matrix_coefficients")] private static partial int Matrix(IntPtr picture);
    internal string Format { get; private set; }="HEVC / libde265 (CPU)";
    [LibraryImport(Library,EntryPoint="de265_get_error_text")] private static partial IntPtr ErrorText(int code);
    [LibraryImport(Library,EntryPoint="de265_get_warning")] private static partial int Warning(IntPtr ctx);
    private NetworkCameraException Failure(string stage,int code)
    {
        string detail=code>0?Marshal.PtrToStringAnsi(ErrorText(code))??"":"";
        if(stage=="decode"&&_context!=IntPtr.Zero){int warning=Warning(_context);if(warning!=0)detail+="; warning "+warning+": "+Marshal.PtrToStringAnsi(ErrorText(warning));}
        return new(NetworkCameraState.VideoDecodeFailed,$"libde265 / {stage} / {code} {detail}");
    }
    internal SoftwareHevcDecoder()
    {
        try
        {
            if(Version()<0x01010300)throw Failure("version",-1);
            _context=New();if(_context==IntPtr.Zero)throw Failure("open",-1);
            var limits=new Limits{Version=1,Pixels=32_000_000,NalBytes=24*1024*1024,SeiMessages=256};SetLimits(_context,in limits);
            SetBool(_context,6,1); // Suppress faulty pictures, not merely damaged-looking output.
            int code=StartThreads(_context,Math.Clamp(Environment.ProcessorCount/2,1,4));
            if(IsOk(code)==0)throw Failure("workers",code);
        }
        catch(Exception ex) when(ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {Dispose();throw new NetworkCameraException(NetworkCameraState.VideoInitializationFailed,"libde265 / DLL / 0x"+ex.HResult.ToString("X8"));}
        catch {Dispose();throw;}
    }
    internal unsafe SKBitmap? Decode(byte[] unit,long time,bool emit)
    {
        if(unit.Length is <=0 or >NetworkCameraSource.MaxFrameBytes)throw Failure("input size",-1);
        int code=0;
        foreach(var nal in AnnexBUnits.Split(unit))
        {
            if(nal.Length<2)throw Failure("NAL size",-1);
            fixed(byte* p=nal.Span)code=Push(_context,p,nal.Length,time,IntPtr.Zero);
            if(IsOk(code)==0)throw Failure("push NAL",code);
        }
        EndFrame(_context);SKBitmap? latest=null;
        try
        {
            for(int i=0;i<1024;i++)
            {
                code=DecodeNative(_context,out int more);int pictures=0;
                while(Peek(_context) is var picture && picture!=IntPtr.Zero)
                {
                    try
                    {
                        if(++pictures>64)throw Failure("picture queue",-1);
                        if(emit){var next=ConvertPicture(picture);if(latest is not null)ScannerPixels.ClearAndDispose(latest);latest=next;}
                    }
                    finally{Release(_context);}
                }
                if(code==13 || more==0 && IsOk(code)!=0)return latest;
                if(code==9 && pictures>0)continue;
                if(IsOk(code)==0)throw Failure("decode",code);
            }
            throw Failure("decode iteration limit",-1);
        }
        catch {if(latest is not null)ScannerPixels.ClearAndDispose(latest);throw;}
    }
    private unsafe SKBitmap ConvertPicture(IntPtr picture)
    {
        int w=Width(picture,0),h=Height(picture,0),bits=Bits(picture,0);
        if(w<=0||h<=0||(long)w*h>32_000_000||bits is <8 or >16)throw Failure("dimensions / depth",bits);
        bool color=Chroma(picture)!=0;
        int cw=0,ch=0,cb=8,cr=8,us=0,vs=0;
        var yPlane=Plane(picture,0,out int ys);IntPtr uPlane=IntPtr.Zero,vPlane=IntPtr.Zero;
        if(color){cw=Width(picture,1);ch=Height(picture,1);cb=Bits(picture,1);cr=Bits(picture,2);uPlane=Plane(picture,1,out us);vPlane=Plane(picture,2,out vs);}
        if(yPlane==IntPtr.Zero||ys<(long)w*(bits>8?2:1)||color&&(uPlane==IntPtr.Zero||vPlane==IntPtr.Zero||cw<=0||ch<=0||cw>w||ch>h||Width(picture,2)!=cw||Height(picture,2)!=ch||cb is <8 or >16||cr is <8 or >16||us<(long)cw*(cb>8?2:1)||vs<(long)cw*(cr>8?2:1)))throw Failure("planes",-1);
        bool full=FullRange(picture)==1,hd=Matrix(picture)==1;int matrix=Matrix(picture);
        Format=$"HEVC / libde265 (CPU, {bits}-bit)";
        var bmp=new SKBitmap(new SKImageInfo(w,h,SKColorType.Bgra8888,SKAlphaType.Opaque));
        try
        {
            if(bmp.GetPixels()==IntPtr.Zero)throw Failure("bitmap",-1);
            static int Value(byte* row,int x,int depth)=>depth==8?row[x]:((ushort*)row)[x]>>(depth-8);
            static byte Clip(int v)=>(byte)Math.Clamp(v,0,255);
            byte* basePixels=(byte*)bmp.GetPixels();int rowBytes=bmp.RowBytes;
            for(int y=0;y<h;y++)
            {
                byte* dest=basePixels+y*rowBytes;byte* yp=(byte*)yPlane+(long)y*ys;
                byte* up=(byte*)uPlane+(color?(long)(y*(long)ch/h)*us:0),vp=(byte*)vPlane+(color?(long)(y*(long)ch/h)*vs:0);
                for(int x=0;x<w;x++)
                {
                    int l=Value(yp,x,bits),cx=color?(int)(x*(long)cw/w):0;
                    int u=color?Value(up,cx,cb)-128:0,v=color?Value(vp,cx,cr)-128:0;
                    int c=full?256*l:298*Math.Max(0,l-16);
                    int r=matrix==9?(full?377:430):full?(hd?403:359):(hd?459:409);
                    int b=matrix==9?(full?482:548):full?(hd?475:454):(hd?541:516);
                    int gu=matrix==9?(full?42:48):full?(hd?48:88):(hd?55:100);
                    int gv=matrix==9?(full?146:167):full?(hd?120:183):(hd?136:208);
                    dest[x*4]=Clip((c+b*u+128)>>8);dest[x*4+1]=Clip((c-gu*u-gv*v+128)>>8);dest[x*4+2]=Clip((c+r*v+128)>>8);dest[x*4+3]=255;
                }
            }
            return bmp;
        }
        catch{ScannerPixels.ClearAndDispose(bmp);throw;}
    }
    public void Dispose(){if(_context!=IntPtr.Zero){Free(_context);_context=IntPtr.Zero;}}
}
