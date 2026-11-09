using KeePassXCCardPrint.Platforms;

namespace KeePassXCCardPrint.Services;

/// <summary>
/// Monitor work areas and saved window positions are PHYSICAL pixels.
/// Avalonia Width/Height and MinWidth/MinHeight are DIP. Never mix these units.
/// The platform host provides native caption/frame insets in physical pixels on top
/// of the requested Avalonia client dimensions.
/// </summary>
internal readonly record struct ScreenWorkArea(int X, int Y, int Width, int Height, double Scaling);
internal readonly record struct NormalWindowPlacement(
    int X, int Y, double WidthDip, double HeightDip,
    double MinWidthDip, double MinHeightDip);

internal static class WindowPlacement
{
    internal const int MarginPx = 16;
    // Native window insets are provided by the active platform host.
    internal static int FrameWidthPx => PlatformServices.Current.WindowFrame.WidthPx;
    internal static int FrameHeightPx => PlatformServices.Current.WindowFrame.HeightPx;

    internal static NormalWindowPlacement Fit(
        ScreenWorkArea area, double requestedWidthDip, double requestedHeightDip,
        int? requestedX, int? requestedY)
    {
        double scale = double.IsFinite(area.Scaling) && area.Scaling > 0
            ? area.Scaling : 1.0;
        int margin = Math.Min(MarginPx, Math.Max(0, Math.Min(area.Width, area.Height) / 24));
        double maxWidthDip = Math.Max(100, (area.Width - margin * 2 - FrameWidthPx) / scale);
        double maxHeightDip = Math.Max(100, (area.Height - margin * 2 - FrameHeightPx) / scale);
        double minWidthDip = Math.Min(960, maxWidthDip);
        double minHeightDip = Math.Min(520, maxHeightDip);
        double widthDip = Math.Clamp(double.IsFinite(requestedWidthDip) ? requestedWidthDip : 1670,
                                      minWidthDip, maxWidthDip);
        double heightDip = Math.Clamp(double.IsFinite(requestedHeightDip) ? requestedHeightDip : 910,
                                       minHeightDip, maxHeightDip);

        int outerWidthPx = (int)Math.Ceiling(widthDip * scale) + FrameWidthPx;
        int outerHeightPx = (int)Math.Ceiling(heightDip * scale) + FrameHeightPx;
        int minX = area.X + margin;
        int minY = area.Y + margin;
        int maxX = Math.Max(minX, area.X + area.Width - margin - outerWidthPx);
        int maxY = Math.Max(minY, area.Y + area.Height - margin - outerHeightPx);
        int centerX = minX + (maxX - minX) / 2;
        int centerY = minY + (maxY - minY) / 2;
        int x = Math.Clamp(requestedX ?? centerX, minX, maxX);
        int y = Math.Clamp(requestedY ?? centerY, minY, maxY);
        return new NormalWindowPlacement(x, y, widthDip, heightDip, minWidthDip, minHeightDip);
    }

    internal static bool Fits(ScreenWorkArea area, NormalWindowPlacement position)
    {
        double scale = area.Scaling > 0 && double.IsFinite(area.Scaling) ? area.Scaling : 1;
        int left = position.X;
        int top = position.Y;
        int right = left + (int)Math.Ceiling(position.WidthDip * scale) + FrameWidthPx;
        int bottom = top + (int)Math.Ceiling(position.HeightDip * scale) + FrameHeightPx;
        return left >= area.X && top >= area.Y &&
               right <= area.X + area.Width && bottom <= area.Y + area.Height;
    }
}
