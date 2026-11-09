using KeePassXCCardPrint.Platforms;
using System.Globalization;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace KeePassXCCardPrint.Rendering;

/// <summary>
/// The ONLY shaping/positioning path for measured and drawn card text. Layout
/// returns glyph IDs, HarfBuzz positions/clusters and advances once. Skia uses
/// positioned text blobs; XPS places paths for the identical glyphs and points.
/// Secret fonts intentionally bypass discretionary ligatures. All text/layouts
/// are ephemeral and never written to a settings file or persistent cache.
/// </summary>
internal static class ShapedTextEngine
{
    private static IEnumerable<string> Elements(string text)
    {
        var scanner = StringInfo.GetTextElementEnumerator(text);
        while (scanner.MoveNext()) yield return scanner.GetTextElement();
    }

    private static SKFont MakeFont(SKTypeface face, float size) =>
        new(face, Math.Max(2f, size));

    private static SKTypeface FallbackFace(bool secret, string weight) =>
        CardTextMetrics.Typeface(secret ? PlatformServices.Current.FallbackSecretFont :
            PlatformServices.Current.FallbackTextFont, weight);

    private static ShapedGlyphRun ShapeOne(string text, SKFont font, bool secret, float originX)
    {
        if (secret)
        {
            // Literal secrets must not acquire fi/ff ligatures or substitutions.
            var glyphs = font.GetGlyphs(text);
            var positions = font.GetGlyphPositions(glyphs, new SKPoint(0, 0));
            return new ShapedGlyphRun(font, glyphs, positions, Array.Empty<uint>(),
                originX, font.MeasureText(text));
        }

        using var shaper = new SKShaper(font.Typeface);
        var result = shaper.Shape(text, font);
        var ids = new ushort[result.Codepoints.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            if (result.Codepoints[i] > ushort.MaxValue)
                throw new InvalidOperationException("HarfBuzz produced an invalid glyph ID.");
            ids[i] = (ushort)result.Codepoints[i];
        }
        return new ShapedGlyphRun(font, ids, result.Points, result.Clusters,
            originX, result.Width);
    }

    /// <summary>
    /// Returns the same runs for measurements, preview/PDF and XPS output.
    /// All points are local to the run origin; the caller supplies the final
    /// baseline and page placement when drawing. Tracking is between Unicode
    /// grapheme clusters, never inside a surrogate/combining sequence.
    /// </summary>
    internal static ShapedTextLayout CreateLayout(string text, float fontSize,
        string family, string weight, float tracking = 0, bool markZero = false)
    {
        if (!float.IsFinite(tracking) || !float.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));

        bool secret = PortableFonts.IsSecretFont(family);
        var primary = MakeFont(CardTextMetrics.Typeface(family, weight), fontSize);
        SKFont? fallback = null;
        try
        {
            var runs = new List<ShapedGlyphRun>();
            var marks = new List<ZeroGlyphMark>();
            if (string.IsNullOrEmpty(text))
                return new ShapedTextLayout(primary, null, runs, marks, 0);

            // Preserve whole-run kerning/shaping where there is no tracking,
            // missing-glyph fallback or zero highlighting.
            if (!markZero && tracking == 0 && primary.ContainsGlyphs(text))
            {
                var whole = ShapeOne(text, primary, secret, 0);
                runs.Add(whole);
                return new ShapedTextLayout(primary, null, runs, marks, whole.Advance);
            }

            var elements = Elements(text).ToArray();
            float x = 0;
            for (int i = 0; i < elements.Length; ++i)
            {
                string part = elements[i];
                SKFont font = primary;
                if (!primary.ContainsGlyphs(part))
                {
                    fallback ??= MakeFont(FallbackFace(secret, weight), fontSize);
                    font = fallback;
                }
                var run = ShapeOne(part, font, secret, x);
                runs.Add(run);
                if (markZero && part == "0")
                {
                    // Same mark geometry as the pre-existing Skia renderer.
                    float radius = Math.Max(.9f, .36f * 7.5f * (25.4f / 72f));
                    run.ZeroMark = new ZeroGlyphMark(x + run.Advance * .5f,
                        -font.Size * .36f, radius);
                    marks.Add(run.ZeroMark.Value);
                }
                x += run.Advance;
                if (i != elements.Length - 1) x += tracking;
            }
            return new ShapedTextLayout(primary, fallback, runs, marks, x);
        }
        catch
        {
            fallback?.Dispose();
            primary.Dispose();
            throw;
        }
    }

    // Identical shaping and width calculation are used by both layout and paint.
    internal static float Measure(string text, float fontSize, string family,
        string weight, float tracking = 0)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        using var layout = CreateLayout(text, fontSize, family, weight, tracking);
        return layout.Width;
    }

    internal static float Draw(SKCanvas canvas, string text, float left, float baseline,
        float pixels, SKColor color, string family, string weight,
        float trackingPixels = 0, bool markZero = false) =>
        Draw(new SkiaCardCanvas(canvas), text, left, baseline, pixels,
            color, family, weight, trackingPixels, markZero);

    internal static float Draw(ICardCanvas canvas, string text, float left, float baseline,
        float pixels, SKColor color, string family, string weight,
        float trackingPixels = 0, bool markZero = false)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        using var layout = CreateLayout(text, pixels, family, weight,
            trackingPixels, markZero);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawGlyphRuns(layout, left, baseline, paint);
        return layout.Width;
    }
}
