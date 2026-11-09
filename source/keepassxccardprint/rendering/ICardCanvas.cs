using SkiaSharp;

namespace KeePassXCCardPrint.Rendering;

/// <summary>Small, purpose-built card drawing vocabulary. Skia preview/PDF and the
/// Microsoft XPS OM writer receive the SAME geometry and same shaped glyphs.
/// Drawing routines own their paints and paths; canvases do not retain them.</summary>
internal interface ICardCanvas
{
    void Save();
    void Restore();
    void Translate(float x,float y);
    void Scale(float x,float y);
    void Scale(float scale);
    void ClipRect(SKRect rect,SKClipOperation operation,bool antialias);
    void ClipRoundRect(SKRoundRect rect,SKClipOperation operation,bool antialias);
    void DrawRect(float x,float y,float width,float height,SKPaint paint);
    void DrawRect(SKRect rect,SKPaint paint);
    void DrawRoundRect(SKRect rect,float radiusX,float radiusY,SKPaint paint);
    void DrawLine(float x0,float y0,float x1,float y1,SKPaint paint);
    void DrawCircle(float x,float y,float radius,SKPaint paint);
    void DrawPath(SKPath path,SKPaint paint);
    void DrawBitmap(SKBitmap bitmap,SKRect destination,SKSamplingOptions sampling);
    void DrawPicture(SKPicture picture);
    void DrawGlyphRuns(ShapedTextLayout layout,float left,float baseline,SKPaint paint);
}

/// <summary>Direct vector path (rather than parallel SKPicture composition).
/// A future backend may implement this without depending on Windows or XPS.</summary>
internal interface IDirectVectorCardCanvas : ICardCanvas
{
    void DrawRadialGradientRect(SKRect rectangle, SKPoint centre, float radius,
        SKColor[] colors, float[] stops);
}

/// <summary>Zero-copy adapter: all existing Skia picture recording, PDF and raster
/// paths stay unchanged except for one small delegating wrapper.</summary>
internal sealed class SkiaCardCanvas(SKCanvas canvas) : ICardCanvas
{
    public void Save()=>canvas.Save();
    public void Restore()=>canvas.Restore();
    public void Translate(float x,float y)=>canvas.Translate(x,y);
    public void Scale(float x,float y)=>canvas.Scale(x,y);
    public void Scale(float scale)=>canvas.Scale(scale);
    public void ClipRect(SKRect rect,SKClipOperation op,bool aa)=>canvas.ClipRect(rect,op,aa);
    public void ClipRoundRect(SKRoundRect rect,SKClipOperation op,bool aa)=>canvas.ClipRoundRect(rect,op,aa);
    public void DrawRect(float x,float y,float w,float h,SKPaint p)=>canvas.DrawRect(x,y,w,h,p);
    public void DrawRect(SKRect r,SKPaint p)=>canvas.DrawRect(r,p);
    public void DrawRoundRect(SKRect r,float rx,float ry,SKPaint p)=>canvas.DrawRoundRect(r,rx,ry,p);
    public void DrawLine(float x,float y,float x2,float y2,SKPaint p)=>canvas.DrawLine(x,y,x2,y2,p);
    public void DrawCircle(float x,float y,float r,SKPaint p)=>canvas.DrawCircle(x,y,r,p);
    public void DrawPath(SKPath path,SKPaint p)=>canvas.DrawPath(path,p);
    public void DrawBitmap(SKBitmap b,SKRect r,SKSamplingOptions sampling)=>canvas.DrawBitmap(b,r,sampling);
    public void DrawPicture(SKPicture picture)=>canvas.DrawPicture(picture);
    public void DrawGlyphRuns(ShapedTextLayout layout,float left,float baseline,SKPaint paint)
    {
        // Skia receives exactly the already shaped glyph IDs/positions. Build
        // one blob for typical text. When zero dots are enabled, flush just
        // before each dot to preserve the original interleaved drawing order.
        using var builder=new SKTextBlobBuilder();
        bool pending=false;
        void Flush()
        {
            if(!pending)return;
            using var blob=builder.Build();
            if(blob is not null)canvas.DrawText(blob,left,baseline,paint);
            pending=false;
        }
        foreach(var run in layout.Runs)
        {
            if(run.Glyphs.Length!=0)
            {
                SKPoint[] positions;
                if(run.OriginX==0)
                    positions=run.Positions;
                else
                {
                    positions=new SKPoint[run.Positions.Length];
                    for(int i=0;i<positions.Length;i++)
                        positions[i]=new SKPoint(run.OriginX+run.Positions[i].X,run.Positions[i].Y);
                }
                builder.AddPositionedRun(run.Glyphs,run.Font,positions);
                pending=true;
            }
            if(run.ZeroMark is { } mark)
            {
                Flush();
                canvas.DrawCircle(left+mark.X,baseline+mark.BaselineOffset,
                    mark.Radius,paint);
            }
        }
        Flush();
    }
}
