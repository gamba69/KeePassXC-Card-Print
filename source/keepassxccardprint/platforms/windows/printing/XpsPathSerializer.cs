using System.Globalization;
using System.Text;
using SkiaSharp;

namespace KeePassXCCardPrint.Platforms.Windows.Printing;

/// <summary>
/// Converts native Skia contour verbs into XPS abbreviated geometry syntax.
/// The two languages look similar, but SVG allows space-separated x/y numbers
/// while XPS requires each point to be a comma-delimited x,y pair (ECMA-388).
/// Operates on the original SKPath, never reparses an SVG string; no raster data
/// or font/text content is retained or written to disk.
/// </summary>
internal static class XpsPathSerializer
{
    private static string Number(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value)>1_000_000)
            throw new InvalidOperationException("Invalid vector coordinate in XPS geometry.");
        return value.ToString("0.######",CultureInfo.InvariantCulture);
    }

    private static string Point(SKPoint p)=>Number(p.X)+","+Number(p.Y);

    internal static string Serialize(SKPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if(path.IsEmpty)return string.Empty;
        // Inverse fills are not representable as bounded XPS geometry. Refuse
        // rather than print a visually incorrect or unexpectedly filled page.
        string rule=path.FillType switch
        {
            SKPathFillType.Winding => "F1",
            SKPathFillType.EvenOdd => "F0",
            _ => throw new NotSupportedException(
                "Inverse SKPath fill rules cannot be encoded as an XPS Path.Data attribute.")
        };
        var text=new StringBuilder(rule);
        var points=new SKPoint[4];
        using var iter=path.CreateRawIterator();
        bool hasGeometry=false;
        while(true)
        {
            switch(iter.Next(points))
            {
                case SKPathVerb.Move:
                    text.Append(" M ").Append(Point(points[0]));
                    hasGeometry=true;
                    break;
                case SKPathVerb.Line:
                    text.Append(" L ").Append(Point(points[1]));
                    break;
                case SKPathVerb.Quad:
                    text.Append(" Q ").Append(Point(points[1])).Append(' ')
                        .Append(Point(points[2]));
                    break;
                case SKPathVerb.Cubic:
                    text.Append(" C ").Append(Point(points[1])).Append(' ')
                        .Append(Point(points[2])).Append(' ')
                        .Append(Point(points[3]));
                    break;
                case SKPathVerb.Conic:
                    // XPS has no rational conic segment. Approximate in vector
                    // quadratic Beziers using Skia's own conic subdivision.
                    // 2^4 quadratics limits curvature error without bitmaps.
                    var quads=SKPath.ConvertConicToQuads(
                        points[0],points[1],points[2],iter.ConicWeight(),4);
                    if(quads.Length<3 || (quads.Length&1)!=1)
                        throw new InvalidOperationException("Invalid conic subdivision.");
                    for(int i=1;i+1<quads.Length;i+=2)
                        text.Append(" Q ").Append(Point(quads[i])).Append(' ')
                            .Append(Point(quads[i+1]));
                    break;
                case SKPathVerb.Close:
                    text.Append(" Z");
                    break;
                case SKPathVerb.Done:
                    return hasGeometry?text.ToString():string.Empty;
                default:
                    throw new NotSupportedException("Unsupported SKPath geometry command.");
            }
        }
    }
}
