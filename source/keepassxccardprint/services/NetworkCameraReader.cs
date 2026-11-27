using SkiaSharp;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KeePassXCCardPrint.Services;

// Bounded, incremental image framing. Never assumes a TCP read is a whole frame.
internal sealed class NetworkCameraReader(Stream stream) : IDisposable
{
    private readonly byte[] _buffer = new byte[32*1024];
    private int _position,_length;
    private const int Limit = NetworkCameraSource.MaxFrameBytes;
    private async ValueTask<int> Byte(CancellationToken ct)
    {
        if(_position==_length){_length=await stream.ReadAsync(_buffer,ct).ConfigureAwait(false);_position=0;if(_length==0)return -1;}
        return _buffer[_position++];
    }
    private async ValueTask<int> Required(CancellationToken ct)
    {int b=await Byte(ct).ConfigureAwait(false);return b>=0?b:throw new EndOfStreamException();}
    private static void Check(long n){if(n>Limit||n<0)throw new NetworkCameraException(NetworkCameraState.Unsupported);}
    private async Task Copy(MemoryStream output,int count,CancellationToken ct)
    {
        Check(output.Length+count);
        while(count>0)
        {
            if(_position==_length){_length=await stream.ReadAsync(_buffer,ct).ConfigureAwait(false);_position=0;if(_length==0)throw new EndOfStreamException();}
            int n=Math.Min(count,_length-_position);output.Write(_buffer,_position,n);_position+=n;count-=n;
        }
    }
    private static byte[] Take(MemoryStream s)=>s.ToArray();
    private static void Wipe(MemoryStream s){if(s.TryGetBuffer(out var b)&&b.Array is not null)CryptographicOperations.ZeroMemory(b.Array);}
    internal async Task<byte[]> ReadBody(CancellationToken ct)
    {
        using var output=new MemoryStream();
        try
        {
            while(true)
            {
                if(_position==_length){_length=await stream.ReadAsync(_buffer,ct).ConfigureAwait(false);_position=0;if(_length==0)break;}
                Check(output.Length+_length-_position);output.Write(_buffer,_position,_length-_position);_position=_length;
            }
            return Take(output);
        }
        finally{Wipe(output);}
    }
    private async Task<string> Line(CancellationToken ct)
    {
        using var output=new MemoryStream();
        while(true)
        {
            int b=await Required(ct).ConfigureAwait(false);
            if(b==10)break;if(output.Length>=8192)throw new NetworkCameraException(NetworkCameraState.Unsupported);output.WriteByte((byte)b);
        }
        return Encoding.ASCII.GetString(output.ToArray()).TrimEnd('\r');
    }
    internal async Task<byte[]> ReadMultipart(string boundary,CancellationToken ct)
    {
        // Some cameras incorrectly include the two leading dashes in the header value.
        string marker="--"+boundary;int lines=0;
        while(true)
        {
            var line=await Line(ct).ConfigureAwait(false);
            if(line==marker||boundary.StartsWith("--",StringComparison.Ordinal)&&line==boundary)break;
            if(line==marker+"--"||boundary.StartsWith("--",StringComparison.Ordinal)&&line==boundary+"--")throw new EndOfStreamException();
            if(++lines>32)throw new NetworkCameraException(NetworkCameraState.Unsupported);
        }
        int? count=null;lines=0;
        while(true)
        {
            var line=await Line(ct).ConfigureAwait(false);if(line.Length==0)break;
            if(++lines>32)throw new NetworkCameraException(NetworkCameraState.Unsupported);
            int colon=line.IndexOf(':');if(colon<1)continue;
            if(line[..colon].Equals("Content-Length",StringComparison.OrdinalIgnoreCase))
            {
                if(count.HasValue||!int.TryParse(line[(colon+1)..].Trim(),out int n)||n<4||n>Limit)throw new NetworkCameraException(NetworkCameraState.Unsupported);
                count=n;
            }
        }
        if(count is not int length)return await ReadJpeg(ct).ConfigureAwait(false);
        using var output=new MemoryStream();try{await Copy(output,length,ct).ConfigureAwait(false);return Take(output);}finally{Wipe(output);}
    }
    // JPEG syntax, not a naive search for FF D9: APP/EXIF segments can contain embedded JPEG thumbnails.
    internal async Task<byte[]> ReadJpeg(CancellationToken ct)
    {
        using var output=new MemoryStream();
        async ValueTask<int> Next(){Check(output.Length+1);int b=await Required(ct).ConfigureAwait(false);output.WriteByte((byte)b);return b;}
        try
        {
            if(await Next()!=0xff||await Next()!=0xd8)throw new NetworkCameraException(NetworkCameraState.Unsupported);
            bool entropy=false;
            while(true)
            {
                int b=await Next();
                if(b!=0xff){if(entropy)continue;throw new NetworkCameraException(NetworkCameraState.Unsupported);}
                int marker;do{marker=await Next();}while(marker==0xff);
                if(entropy&&(marker==0||marker is >=0xd0 and <=0xd7))continue;
                if(marker==0xd9)return Take(output);
                if(marker==0xd8||marker==0)throw new NetworkCameraException(NetworkCameraState.Unsupported);
                if(marker==1||marker is >=0xd0 and <=0xd7){entropy=false;continue;}
                int high=await Next();int low=await Next();int size=(high<<8)|low;
                if(size<2)throw new NetworkCameraException(NetworkCameraState.Unsupported);
                await Copy(output,size-2,ct).ConfigureAwait(false);entropy=marker==0xda;
            }
        }
        finally{Wipe(output);}
    }
    // FFmpeg BMP image2pipe: uncompressed BGRA plus dimensions per frame.
    // Unlike headerless rawvideo this also survives source resolution changes.
    private async Task ReadExactly(Memory<byte> output,CancellationToken ct)
    {
        int at=0;
        while(at<output.Length)
        {
            if(_position==_length){_length=await stream.ReadAsync(_buffer,ct).ConfigureAwait(false);_position=0;if(_length==0)throw new EndOfStreamException();}
            int n=Math.Min(output.Length-at,_length-_position);_buffer.AsMemory(_position,n).CopyTo(output[at..]);_position+=n;at+=n;
        }
    }
    internal async Task<SKBitmap> ReadBgra(CancellationToken ct)
    {
        var header=new byte[54];await ReadExactly(header,ct).ConfigureAwait(false);
        int w=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(18)),signedHeight=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(22));
        long h=Math.Abs((long)signedHeight),pixels=(long)w*h;
        if(header[0]!='B'||header[1]!='M'||BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(10))!=54||BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(14))!=40||
            BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26))!=1||BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28))!=32||BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(30))!=0||
            w<1||h<1||pixels>32_000_000||BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(2))!=54+pixels*4)throw new NetworkCameraException(NetworkCameraState.Unsupported,"Invalid uncompressed BGRA frame header");
        var bitmap=new SKBitmap(new SKImageInfo(w,(int)h,SKColorType.Bgra8888,SKAlphaType.Opaque));
        byte[]? row=null;
        try
        {
            if(bitmap.GetPixels()==IntPtr.Zero)throw new NetworkCameraException(NetworkCameraState.Unsupported);
            int stride=checked(w*4);row=System.Buffers.ArrayPool<byte>.Shared.Rent(stride);
            for(int y=0;y<h;y++)
            {
                await ReadExactly(row.AsMemory(0,stride),ct).ConfigureAwait(false);
                // BMP stores positive heights bottom-up. Ignore its unused alpha byte.
                CopyRow(bitmap,row,stride,signedHeight>0?(int)h-1-y:y);
            }
            return bitmap;
        }
        catch{ScannerPixels.ClearAndDispose(bitmap);throw;}
        finally{if(row is not null)System.Buffers.ArrayPool<byte>.Shared.Return(row,clearArray:true);}
    }
    private static unsafe void CopyRow(SKBitmap bitmap,byte[] row,int stride,int y)
    {
        var dest=new Span<byte>((byte*)bitmap.GetPixels()+y*bitmap.RowBytes,stride);
        row.AsSpan(0,stride).CopyTo(dest);for(int x=3;x<stride;x+=4)dest[x]=255;
    }
    public void Dispose()=>CryptographicOperations.ZeroMemory(_buffer);
}
