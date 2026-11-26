using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace KeePassXCCardPrint.Services;

// UI-only dimensions in DIPs. Never change print geometry or the saved toolbar
// preference in response to a temporary viewport constraint.
internal static class AdaptiveViewport
{
    internal static double MappingFraction(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, .25, .75) : .45;

    internal static double SourceConnectionHeight(double height) =>
        Math.Max(32, Math.Min(height*.65, height-160)-40);

    internal static double RestrictionsHeight(double height) =>
        Math.Max(0, Math.Min(150, Math.Min(height*.3, height-190)));

    // 29-DIP buttons, 2-DIP gaps within pairs, 8-DIP gaps between groups,
    // 4-DIP host padding plus a small rounding allowance. The duplex group
    // contains two buttons and is omitted as a unit in simplex.
    internal static bool UseSideToolbar(string preference, double width, bool duplex) =>
        preference == "side" || (width > 0 && width < (duplex ? 420 : 350));

    internal static double DialogHeight(double workingHeightPx, double scaling) =>
        Math.Max(120, workingHeightPx / Math.Max(.1, scaling) - 40);

    internal static void LimitDialog(Window window)
    {
        window.Opened += (_, _) =>
        {
            var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
            if (screen is null) return;
            double limit = Math.Min(window.MaxHeight, DialogHeight(screen.WorkingArea.Height, screen.Scaling));
            window.MinHeight = Math.Min(window.MinHeight, limit);
            window.MaxHeight = limit;
            if (double.IsFinite(window.Height) && window.Height > limit) window.Height = limit;
            Dispatcher.UIThread.Post(() =>
            {
                if (!window.IsVisible) return;
                var area = screen.WorkingArea;
                int height = (int)Math.Ceiling(window.Bounds.Height * screen.Scaling);
                int y = Math.Clamp(window.Position.Y, area.Y, Math.Max(area.Y,area.Bottom-height-32));
                if (window.Position.Y != y) window.Position = new PixelPoint(window.Position.X,y);
            }, DispatcherPriority.Loaded);
        };
    }
}
