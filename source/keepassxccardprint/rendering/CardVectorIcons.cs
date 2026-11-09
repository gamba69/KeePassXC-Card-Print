using SkiaSharp;

namespace KeePassXCCardPrint.Rendering;

/// <summary>Microsoft Fluent 24px Regular icons shared with the editor picker.</summary>
internal static class CardVectorIcons
{
    private static readonly object GeometryLock = new();
    private static readonly Dictionary<string, SKPath> GeometryCache = new(StringComparer.Ordinal);

    private static SKPath? Geometry(string id)
    {
        lock (GeometryLock)
        {
            if (GeometryCache.TryGetValue(id, out var cached)) return cached;
            var source = FluentCardGlyphs.PathData(id);
            if (source is null) return null;
            var path = SKPath.ParseSvgPathData(source)
                ?? throw new InvalidOperationException($"Invalid Fluent SVG path for '{id}'.");
            GeometryCache.Add(id, path);
            return path;
        }
    }

    internal static void Draw(SKCanvas canvas,string id,float centerX,float centerY,
        float sizePx,SKColor color)
        => Draw(new SkiaCardCanvas(canvas),id,centerX,centerY,sizePx,color);

    internal static void Draw(ICardCanvas canvas, string id, float centerX, float centerY,
        float sizePx, SKColor color)
    {
        if (id == "none" || sizePx < 1f || color.Alpha == 0) return;
        var vector = Geometry(id);
        if (vector is null) return;
        using var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.Save();
        try
        {
            canvas.Translate(centerX - sizePx * .5f, centerY - sizePx * .5f);
            canvas.Scale(sizePx / 24f);
            canvas.DrawPath(vector, paint);
        }
        finally { canvas.Restore(); }
    }
}
