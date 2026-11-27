using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;

namespace KeePassXCCardPrint;

// Common bounds for every secondary application window. Screen working areas
// are physical pixels; layout constraints are DIPs on the target monitor.
internal static class DialogScreenPolicy
{
    private static readonly ConditionalWeakTable<Window, Limits> Tracked = new();
    private static bool _installed;

    internal static void Install(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_installed) return;
        _installed = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, args) =>
        {
            if (window is MainWindow) return;
            if (Tracked.TryGetValue(window, out _)) return;
            var limits = new Limits(window);
            Tracked.Add(window, limits);
            var owner = desktop.Windows.FirstOrDefault(w => w.IsActive && w != window) ?? desktop.MainWindow;
            limits.BeforeShow(owner == window ? null : owner);
        });
    }

    internal static Size ClientLimit(PixelRect area, double scaling, Size decoration)
    {
        scaling = Math.Max(0.1, scaling);
        // Leave a little space for rounding, native frame and shadows.
        return new Size(Math.Max(1, Math.Floor(area.Width / scaling - Math.Max(0, decoration.Width) - 16)),
            Math.Max(1, Math.Floor(area.Height / scaling - Math.Max(0, decoration.Height) - 16)));
    }

    private sealed class Limits
    {
        private readonly Window _window;
        private readonly double _minWidth, _minHeight, _maxWidth, _maxHeight;
        private bool _pending, _correcting;
        private PixelRect? _lastArea;
        private double _lastScale;

        internal Limits(Window window)
        {
            _window = window;
            _minWidth = window.MinWidth; _minHeight = window.MinHeight;
            _maxWidth = window.MaxWidth; _maxHeight = window.MaxHeight;
        }

        internal void BeforeShow(Window? owner)
        {
            // A simple content stack otherwise gets clipped when its desired
            // height exceeds the screen. Preserve existing grids/docks/scroll
            // viewers (many already have a fixed footer and a scrolling body).
            if (_window.Content is StackPanel stack)
            {
                _window.Content = null;
                _window.Content = Scroll(stack);
            }
            else if (_window.Content is Border border && border.Child is StackPanel child)
            {
                border.Child = null;
                border.Child = Scroll(child);
            }
            var screen = owner?.Screens.ScreenFromWindow(owner) ?? _window.Screens.ScreenFromWindow(_window) ?? _window.Screens.Primary;
            if (screen is not null) SetLimits(screen, new Size(24, 64));
            _window.Opened += (_, _) => QueueCheck(true);
            _window.SizeChanged += (_, _) => QueueCheck(true);
            _window.PositionChanged += (_, _) => QueueCheck(false);
        }

        private static ScrollViewer Scroll(Control content) => new()
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        private void SetLimits(Screen screen, Size decoration)
        {
            var limit = ClientLimit(screen.WorkingArea, screen.Scaling, decoration);
            double maxWidth = Math.Min(_maxWidth, limit.Width), maxHeight = Math.Min(_maxHeight, limit.Height);
            // Lower minimums first so an unusually small or high-DPI monitor
            // cannot force the window outside its working area.
            _window.MinWidth = Math.Min(_minWidth, maxWidth); _window.MinHeight = Math.Min(_minHeight, maxHeight);
            _window.MaxWidth = maxWidth; _window.MaxHeight = maxHeight;
            if (double.IsFinite(_window.Width) && _window.Width > maxWidth) _window.Width = maxWidth;
            if (double.IsFinite(_window.Height) && _window.Height > maxHeight) _window.Height = maxHeight;
            _lastArea = screen.WorkingArea; _lastScale = screen.Scaling;
        }

        private void QueueCheck(bool clampPosition)
        {
            if (_pending || _correcting) return;
            _pending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _pending = false;
                if (!_window.IsVisible || _window.WindowState != WindowState.Normal) return;
                var screen = _window.Screens.ScreenFromWindow(_window) ?? _window.Screens.Primary;
                if (screen is null) return;
                bool changed = !Equals(_lastArea, screen.WorkingArea) || _lastScale != screen.Scaling;
                _correcting = true;
                try
                {
                    var frame = _window.FrameSize ?? _window.ClientSize;
                    var decoration = new Size(Math.Max(0, frame.Width - _window.ClientSize.Width), Math.Max(0, frame.Height - _window.ClientSize.Height));
                    SetLimits(screen, decoration);
                    if (!clampPosition && !changed) return; // do not fight ordinary dragging
                    var area = screen.WorkingArea;
                    int width = (int)Math.Ceiling(Math.Min(frame.Width, _window.MaxWidth + decoration.Width) * screen.Scaling);
                    int height = (int)Math.Ceiling(Math.Min(frame.Height, _window.MaxHeight + decoration.Height) * screen.Scaling);
                    var pos = _window.Position;
                    var next = new PixelPoint(Math.Clamp(pos.X, area.X, Math.Max(area.X, area.Right - width)),
                        Math.Clamp(pos.Y, area.Y, Math.Max(area.Y, area.Bottom - height)));
                    if (next != pos) _window.Position = next;
                }
                finally { _correcting = false; }
            }, DispatcherPriority.Background);
        }
    }
}
