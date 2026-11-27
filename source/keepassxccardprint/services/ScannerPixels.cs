using SkiaSharp;

namespace KeePassXCCardPrint.Services;

// One immutable-by-contract pixel buffer, shared by the latest/best frame,
// preview and recognition workers. Each lease can be disposed independently.
internal sealed class ScannerPixels : IDisposable
{
    private sealed class Storage(SKBitmap bitmap)
    {
        internal readonly SKBitmap Bitmap=bitmap;
        internal int References=1;
    }
    private Storage? _storage;
    internal ScannerPixels(SKBitmap bitmap)=>_storage=new Storage(bitmap);
    private ScannerPixels(Storage storage)=>_storage=storage;
    internal SKBitmap Bitmap=>_storage?.Bitmap??throw new ObjectDisposedException(nameof(ScannerPixels));
    // Share only while the caller owns a live lease (or under the sample lock).
    internal ScannerPixels Share()
    {
        var storage=_storage??throw new ObjectDisposedException(nameof(ScannerPixels));
        Interlocked.Increment(ref storage.References);
        return new ScannerPixels(storage);
    }
    public void Dispose()
    {
        var storage=Interlocked.Exchange(ref _storage,null);
        if(storage is not null&&Interlocked.Decrement(ref storage.References)==0)ClearAndDispose(storage.Bitmap);
    }
    internal static unsafe void ClearAndDispose(SKBitmap bitmap)
    {
        try
        {
            if(bitmap.GetPixels()!=IntPtr.Zero)
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(new Span<byte>((void*)bitmap.GetPixels(),bitmap.ByteCount));
        }
        finally{bitmap.Dispose();}
    }
    internal static unsafe SKBitmap CopyBgra(IntPtr pixels,int width,int height)
    {
        if(pixels==IntPtr.Zero||width<1||height<1||(long)width*height>32_000_000)throw new NetworkCameraException(NetworkCameraState.Unsupported);
        var bitmap=new SKBitmap(new SKImageInfo(width,height,SKColorType.Bgra8888,SKAlphaType.Opaque));
        try
        {
            if(bitmap.GetPixels()==IntPtr.Zero)throw new NetworkCameraException(NetworkCameraState.Unsupported);
            int stride=checked(width*4),rowBytes=bitmap.RowBytes;
            for(int y=0;y<height;y++)new ReadOnlySpan<byte>((byte*)pixels+y*stride,stride).CopyTo(new Span<byte>((byte*)bitmap.GetPixels()+y*rowBytes,stride));
            return bitmap;
        }
        catch{ClearAndDispose(bitmap);throw;}
    }
}
