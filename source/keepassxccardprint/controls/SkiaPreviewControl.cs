using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using KeePassXCCardPrint.Rendering;
using SkiaSharp;
using System.Threading;

namespace KeePassXCCardPrint.Controls;

/// <summary>
/// Replays the exact print SKPictures directly into Avalonia's Skia canvas.
/// Both frames of a crossfade are drawn in ONE operation: native custom Skia
/// drawing must not rely on a parent Control.Opacity or a second overlapping
/// custom control to supply the old picture's alpha.
/// </summary>
public sealed class SkiaPreviewControl : Control
{
    public static readonly StyledProperty<SkiaPreviewScene?> SceneProperty =
        AvaloniaProperty.Register<SkiaPreviewControl,SkiaPreviewScene?>(nameof(Scene));
    public static readonly StyledProperty<SkiaPreviewScene?> PreviousSceneProperty =
        AvaloniaProperty.Register<SkiaPreviewControl,SkiaPreviewScene?>(nameof(PreviousScene));
    public static readonly StyledProperty<bool> MonochromeProperty =
        AvaloniaProperty.Register<SkiaPreviewControl,bool>(nameof(Monochrome));
    public bool Monochrome
    {
        get=>GetValue(MonochromeProperty);
        set=>SetValue(MonochromeProperty,value);
    }

    public static readonly StyledProperty<double> PreviousOpacityProperty =
        AvaloniaProperty.Register<SkiaPreviewControl,double>(nameof(PreviousOpacity),0d);

    private SkiaPreviewScene? _retainedScene;
    private SkiaPreviewScene? _retainedPreviousScene;

    static SkiaPreviewControl()
    {
        // The opacity change MUST invalidate the picture itself on every tick,
        // not merely change a parent compositing node's visual property.
        AffectsRender<SkiaPreviewControl>(SceneProperty,PreviousSceneProperty,PreviousOpacityProperty,MonochromeProperty);
    }

    public SkiaPreviewScene? Scene
    {
        get=>GetValue(SceneProperty);
        set=>SetValue(SceneProperty,value);
    }
    public SkiaPreviewScene? PreviousScene
    {
        get=>GetValue(PreviousSceneProperty);
        set=>SetValue(PreviousSceneProperty,value);
    }
    public double PreviousOpacity
    {
        get=>GetValue(PreviousOpacityProperty);
        set=>SetValue(PreviousOpacityProperty,value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if(change.Property==SceneProperty)
        {
            var next=change.GetNewValue<SkiaPreviewScene?>();
            next?.Retain();
            var old=_retainedScene;
            _retainedScene=next;
            old?.Dispose();
        }
        else if(change.Property==PreviousSceneProperty)
        {
            var next=change.GetNewValue<SkiaPreviewScene?>();
            next?.Retain();
            var old=_retainedPreviousScene;
            _retainedPreviousScene=next;
            old?.Dispose();
        }
        base.OnPropertyChanged(change);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if(Bounds.Width<=0 || Bounds.Height<=0)return;
        var current=_retainedScene;
        var previous=_retainedPreviousScene;
        if(current is null && previous is null)return;
        context.Custom(new PictureDrawOperation(new Rect(Bounds.Size),current,previous,PreviousOpacity,Monochrome));
    }

    private sealed class PictureDrawOperation : ICustomDrawOperation
    {
        private readonly SkiaPreviewScene? _current;
        private readonly SkiaPreviewScene? _previous;
        private readonly double _previousOpacity;
        private readonly bool _monochrome;
        private int _disposed;

        public PictureDrawOperation(Rect bounds,SkiaPreviewScene? current,
            SkiaPreviewScene? previous,double previousOpacity,bool monochrome)
        {
            Bounds=bounds;
            _current=current?.Retain();
            _previous=previous?.Retain();
            _previousOpacity=Math.Clamp(previousOpacity,0d,1d);
            _monochrome=monochrome;
        }

        public Rect Bounds { get; }
        public bool HitTest(Point point)=>false;
        public bool Equals(ICustomDrawOperation? other)=>false;

        public void Render(ImmediateDrawingContext context)
        {
            using var lease=context.TryGetFeature<ISkiaSharpApiLeaseFeature>()?.Lease();
            if(lease is null)return;
            SKCanvas canvas=lease.SkCanvas;
            // Standard luminance weights; matrix preserves alpha and all geometry.
            using var grayscale=_monochrome ? SKColorFilter.CreateColorMatrix(new float[]
            {
                .2126f,.7152f,.0722f,0,0,
                .2126f,.7152f,.0722f,0,0,
                .2126f,.7152f,.0722f,0,0,
                0,0,0,1,0
            }) : null;
            using var grayPaint=grayscale is null ? null : new SKPaint { ColorFilter=grayscale };
            canvas.Save();
            try
            {
                canvas.ClipRect(new SKRect(0,0,(float)Bounds.Width,(float)Bounds.Height));
                // Current page is opaque beneath the previous page. On the
                // first fade frame draw only the old page to prevent a flash.
                if(_current is not null && (_previous is null || _previousOpacity<1))
                    DrawScene(canvas,_current,grayPaint);
                if(_previous is not null && _previousOpacity>0)
                {
                    if(_previousOpacity>=1)
                        DrawScene(canvas,_previous,grayPaint);
                    else
                    {
                        // SKCanvas.DrawPicture(..., SKPaint) composites the
                        // *whole* display list using this alpha. An outside
                        // Avalonia Opacity cannot reliably fade custom draw ops.
                        using var paint=new SKPaint
                        {
                            ColorFilter=grayscale,
                            Color=SKColors.White.WithAlpha((byte)Math.Clamp(
                                (int)Math.Round(_previousOpacity*255),0,255))
                        };
                        DrawScene(canvas,_previous,paint);
                    }
                }
            }
            finally { canvas.Restore(); }
        }

        private void DrawScene(SKCanvas canvas,SkiaPreviewScene scene,SKPaint? paint)
        {
            canvas.Save();
            try
            {
                canvas.Scale((float)Bounds.Width/scene.Width,(float)Bounds.Height/scene.Height);
                canvas.DrawPicture(scene.Picture,paint);
            }
            finally { canvas.Restore(); }
        }

        public void Dispose()
        {
            if(Interlocked.Exchange(ref _disposed,1)!=0)return;
            _current?.Dispose();
            _previous?.Dispose();
        }
    }
}
