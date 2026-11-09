using Avalonia;
using Avalonia.Controls;

namespace KeePassXCCardPrint.Controls;

/// <summary>
/// Arranges ordinary TabItems with the final tab anchored to the right edge.
/// At narrow widths the left tabs contract proportionally, so the final tab
/// stays accessible rather than overlapping it. Not platform-specific.
/// </summary>
public sealed class RightAnchoredTabHeaderPanel : Panel
{
    private const double Separator = 14;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0;
        double height = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        if (Children.Count > 1) width += Separator;
        // The tab bar stretches to the entire available pane width.
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0) return finalSize;
        var last = Children.Count - 1;
        var right = Children[last];
        var rightWidth = right.DesiredSize.Width;
        var totalLeft = 0d;
        for (var i = 0; i < last; i++) totalLeft += Children[i].DesiredSize.Width;

        var leftWidth = Math.Max(0d, finalSize.Width - rightWidth - Separator);
        // Contraction matters only when the panes are dragged unusually narrow.
        var factor = totalLeft > 0d ? Math.Min(1d, leftWidth / totalLeft) : 1d;
        var x = 0d;
        for (var i = 0; i < last; i++)
        {
            var child = Children[i];
            var width = child.DesiredSize.Width * factor;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }
        right.Arrange(new Rect(Math.Max(x, finalSize.Width - rightWidth), 0,
                               Math.Min(rightWidth, finalSize.Width), finalSize.Height));
        return finalSize;
    }
}
