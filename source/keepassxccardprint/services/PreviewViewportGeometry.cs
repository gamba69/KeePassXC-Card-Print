using Avalonia;

namespace KeePassXCCardPrint.Services;

/// <summary>Pure screen-space anchor maths for layout-independent preview zoom.</summary>
internal static class PreviewViewportGeometry
{
    internal const double MinimumScale=.05;
    internal const double MaximumScale=6.0;

    // Continuous wheel/trackpad deltas are intentionally not rounded to notches.
    internal static double WheelZoom(double current,double delta,int percentPerNotch)
    {
        if(!double.IsFinite(current) || current<=0 || !double.IsFinite(delta))
            throw new ArgumentOutOfRangeException(nameof(current));
        double speed=1+Math.Clamp(percentPerNotch,2,30)/100d;
        return Math.Clamp(current*Math.Pow(speed,delta),MinimumScale,MaximumScale);
    }

    // The complete sheet/card fits inside the available client canvas (scrollbars excluded).
    // The result does not modify the print layout or create a raster image.
    internal static double FitScale(double viewportWidth,double viewportHeight,
        double documentWidth,double documentHeight,double margin=12)
    {
        if(viewportWidth<=0 || viewportHeight<=0 || documentWidth<=0 || documentHeight<=0)
            return 1;
        double width=Math.Max(1,viewportWidth-2*margin);
        double height=Math.Max(1,viewportHeight-2*margin);
        return Math.Clamp(Math.Min(width/documentWidth,height/documentHeight),
            MinimumScale,MaximumScale);
    }

    internal static Point CenteredOrigin(double viewportWidth,double viewportHeight,
        double documentWidth,double documentHeight,double scale) => new(
            (viewportWidth-documentWidth*scale)/2d,
            (viewportHeight-documentHeight*scale)/2d);

    internal static Point ZoomAtCursor(Point origin,Point cursor,double before,double after)
    {
        if(before<=0 || after<=0 || !double.IsFinite(before) || !double.IsFinite(after))
            throw new ArgumentOutOfRangeException(nameof(after));
        double ratio=after/before;
        return new Point(cursor.X-(cursor.X-origin.X)*ratio,
                         cursor.Y-(cursor.Y-origin.Y)*ratio);
    }
}
