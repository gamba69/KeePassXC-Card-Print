using KeePassXCCardPrint.Rendering;

namespace KeePassXCCardPrint.Services;

/// <summary>
/// Print both sides as independent one-sided pages in one job, in physical
/// sheet order. No sheet flip or manual pause is performed.
/// </summary>
internal static class SingleSidedPrintPlan
{
    internal static IReadOnlyList<CardPrintSheet> Create(IReadOnlyList<CardPrintSheet> pages)
    {
        if (pages.Count == 0) return [];
        var ordered=pages.OrderBy(s=>s.SheetNumber).ThenBy(s=>s.Reverse).ToArray();
        for(int i=0;i<ordered.Length;i++)
        {
            if(i>0 && ordered[i-1].SheetNumber==ordered[i].SheetNumber &&
                ordered[i-1].Reverse==ordered[i].Reverse)
                throw new InvalidOperationException("Повторяется одна сторона печатного листа.");
        }
        return ordered;
    }
}
