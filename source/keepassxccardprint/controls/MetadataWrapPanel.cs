using Avalonia;
using Avalonia.Controls;

namespace KeePassXCCardPrint.Controls;

// Children alternate between a metadata fragment and a separator. Separators
// occupy space only when both adjacent fragments are on the same visual row.
public sealed class MetadataWrapPanel : Panel
{
    internal static (Rect[] Slots, Size Extent) Plan(IReadOnlyList<Size> sizes, double width)
    {
        var slots = new Rect[sizes.Count];
        double x = 0, y = 0, rowHeight = 0, maxWidth = 0;
        for (int i = 0; i < sizes.Count; i += 2)
        {
            var item = sizes[i];
            double itemWidth = Math.Min(width, item.Width);
            if (i > 0)
            {
                var separator = sizes[i - 1];
                if (x + separator.Width + itemWidth > width)
                {
                    y += rowHeight;
                    x = 0;
                    rowHeight = 0;
                }
                else
                {
                    slots[i - 1] = new Rect(x, y, separator.Width, separator.Height);
                    x += separator.Width;
                    rowHeight = Math.Max(rowHeight, separator.Height);
                }
            }
            slots[i] = new Rect(x, y, itemWidth, item.Height);
            x += itemWidth;
            rowHeight = Math.Max(rowHeight, item.Height);
            maxWidth = Math.Max(maxWidth, x);
        }
        return (slots, new Size(maxWidth, y + rowHeight));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = Math.Max(0, availableSize.Width);
        var sizes = new Size[Children.Count];
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            if (i % 2 == 0 && child.DesiredSize.Width > width)
                child.Measure(new Size(width, availableSize.Height));
            sizes[i] = child.DesiredSize;
        }
        return Plan(sizes, width).Extent;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var slots = Plan(Children.Select(c => c.DesiredSize).ToArray(), finalSize.Width).Slots;
        for (int i = 0; i < Children.Count; i++)
        {
            // A skipped separator has a zero slot; opacity also prevents a
            // text presenter with a natural minimum size from painting there.
            if (i % 2 != 0) Children[i].Opacity = slots[i].Width > 0 ? 1 : 0;
            Children[i].Arrange(slots[i]);
        }
        return finalSize;
    }
}
