using SkiaSharp;

namespace KeePassXCCardPrint.Rendering;

// Geometry only: never retain a record, its strings, or a field value in hit metadata.
public sealed record PreviewHitRegion(string Section, string Group, SKRect Bounds)
{
    internal PreviewHitRegion Translate(float x, float y) => new(Section, Group,
        new SKRect(Bounds.Left+x, Bounds.Top+y, Bounds.Right+x, Bounds.Bottom+y));
    internal static bool CanCommit(PreviewHitRegion? pressed,PreviewHitRegion? released,bool control,bool dragged,double dx,double dy) =>
        control && !dragged && pressed is not null && pressed==released && Math.Abs(dx)<4 && Math.Abs(dy)<4;
    internal bool Contains(float x, float y) => x >= Bounds.Left && x <= Bounds.Right && y >= Bounds.Top && y <= Bounds.Bottom;
}
