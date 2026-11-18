using Avalonia;
using Avalonia.Controls;

namespace KeePassXCCardPrint.Controls;

/// <summary>
/// The final TWO tabs form one right-aligned group. Unused width becomes a
/// flexible gap before Settings; no gap is inserted between Settings and About.
/// Extremely narrow sizes contract left headers first, then both right headers.
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
        if (Children.Count > 2) width += Separator;
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0) return finalSize;
        var rightStart = Math.Max(0, Children.Count - 2);
        var leftTotal = 0d;
        var rightTotal = 0d;
        for (var i = 0; i < Children.Count; ++i)
            if (i < rightStart) leftTotal += Children[i].DesiredSize.Width;
            else rightTotal += Children[i].DesiredSize.Width;
        // Preserve the two right headings as long as possible. Do not overlap
        // even if the parent is forced narrower than their combined width.
        var rightScale = rightTotal > finalSize.Width && rightTotal > 0
            ? Math.Max(0, finalSize.Width) / rightTotal : 1d;
        var rightWidth = rightTotal * rightScale;
        var leftAvailable = Math.Max(0d, finalSize.Width - rightWidth -
            (rightStart > 0 ? Separator : 0));
        var leftScale = leftTotal > 0 ? Math.Min(1d, leftAvailable / leftTotal) : 1d;
        var x = 0d;
        for (var i = 0; i < rightStart; ++i)
        {
            var width = Children[i].DesiredSize.Width * leftScale;
            Children[i].Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }
        x = Math.Max(x, finalSize.Width - rightWidth);
        for (var i = rightStart; i < Children.Count; ++i)
        {
            var width = Children[i].DesiredSize.Width * rightScale;
            Children[i].Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }
        return finalSize;
    }
}
