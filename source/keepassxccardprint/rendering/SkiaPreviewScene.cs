using System.Threading;
using SkiaSharp;

namespace KeePassXCCardPrint.Rendering;

/// <summary>
/// Immutable print-coordinate display list. Recording occurs on worker threads;
/// Avalonia replays it directly on its Skia canvas with no PNG or pixel copy.
/// Reference-counting protects the picture when Avalonia retains a draw operation
/// across a view-model change or a crossfade. Never write these scenes to disk.
/// </summary>
public sealed class SkiaPreviewScene : IDisposable
{
    private int _references=1;
    private readonly SKPicture _picture;
    public float Width { get; }
    public float Height { get; }
    internal SKPicture Picture => _picture;

    internal SkiaPreviewScene(SKPicture picture,float width,float height)
    {
        _picture=picture;
        Width=width;
        Height=height;
    }

    public SkiaPreviewScene Retain()
    {
        while(true)
        {
            int count=Volatile.Read(ref _references);
            if(count<=0)throw new ObjectDisposedException(nameof(SkiaPreviewScene));
            if(Interlocked.CompareExchange(ref _references,count+1,count)==count)return this;
        }
    }

    public void Dispose()
    {
        int remaining=Interlocked.Decrement(ref _references);
        if(remaining==0)_picture.Dispose();
        else if(remaining<0)throw new ObjectDisposedException(nameof(SkiaPreviewScene));
    }
}
