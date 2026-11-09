using SkiaSharp;

namespace KeePassXCCardPrint.Rendering;

/// <summary>
/// Output of one text-shaping pass. Glyph IDs and local glyph origins are consumed
/// without reshaping by both Skia and XPS. Font is borrowed from ShapedTextLayout;
/// this object is only valid while that layout remains alive.
/// No source text, decrypted values, or shaped layouts are persisted or cached.
/// </summary>
internal sealed class ShapedGlyphRun
{
    internal ShapedGlyphRun(SKFont font, ushort[] glyphs, SKPoint[] positions,
        uint[] clusters, float originX, float advance)
    {
        if (glyphs.Length != positions.Length ||
            (clusters.Length != 0 && clusters.Length != glyphs.Length))
            throw new ArgumentException("Shaped glyph and position counts must agree.");
        if (!float.IsFinite(originX) || !float.IsFinite(advance) ||
            positions.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y)))
            throw new ArgumentException("Nonfinite glyph positions are not printable.");

        Font = font;
        Glyphs = glyphs;
        Positions = positions;
        Clusters = clusters;
        OriginX = originX;
        Advance = advance;
    }

    internal SKFont Font { get; }
    internal ushort[] Glyphs { get; }
    /// <summary>Positions relative to the run's origin and baseline.</summary>
    internal SKPoint[] Positions { get; }
    /// <summary>HarfBuzz cluster indices (empty for literal nonshaped secrets).</summary>
    internal uint[] Clusters { get; }
    internal float OriginX { get; }
    internal float Advance { get; }
    internal ZeroGlyphMark? ZeroMark { get; set; }
}

internal readonly record struct ZeroGlyphMark(float X, float BaselineOffset, float Radius);

/// <summary>
/// Owns the native font instances for its glyph runs. A layout is short lived:
/// create, measure/render with either backend, dispose. Never keep it across vault
/// changes, frame rendering, or print-job boundaries.
/// </summary>
internal sealed class ShapedTextLayout : IDisposable
{
    private bool _disposed;

    internal ShapedTextLayout(SKFont primary, SKFont? fallback,
        IReadOnlyList<ShapedGlyphRun> runs, IReadOnlyList<ZeroGlyphMark> zeroMarks, float width)
    {
        PrimaryFont = primary;
        FallbackFont = fallback;
        Runs = runs;
        ZeroMarks = zeroMarks;
        Width = width;
    }

    private SKFont PrimaryFont { get; }
    private SKFont? FallbackFont { get; }
    internal IReadOnlyList<ShapedGlyphRun> Runs { get; }
    internal IReadOnlyList<ZeroGlyphMark> ZeroMarks { get; }
    internal float Width { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var run in Runs)
        {
            Array.Clear(run.Glyphs);
            Array.Clear(run.Positions);
            Array.Clear(run.Clusters);
        }
        FallbackFont?.Dispose();
        PrimaryFont.Dispose();
    }
}
