using KeePassXCCardPrint.Rendering;

namespace KeePassXCCardPrint.Services;

/// <summary>Physical sheet pairs, never bulk front/back stacks.
/// Validate all pairs before any sheet is handed to the platform print spooler.</summary>
internal static class ManualDuplexPlan
{
    internal sealed record Sheet(int Number, CardPrintSheet Front, CardPrintSheet Back);

    internal static IReadOnlyList<Sheet> Create(IReadOnlyList<CardPrintSheet> pages)
    {
        if (pages.Count == 0) return [];
        var plan = new List<Sheet>();
        foreach (var group in pages.GroupBy(x => x.SheetNumber).OrderBy(x => x.Key))
        {
            var all = group.ToArray();
            var faces = all.Where(x => !x.Reverse).ToArray();
            var backs = all.Where(x => x.Reverse).ToArray();
            if (faces.Length != 1 || backs.Length != 1 ||
                faces[0].Paper != backs[0].Paper ||
                !faces[0].Slots.Select(x => x.CardIndex)
                    .SequenceEqual(backs[0].Slots.Select(x => x.CardIndex)))
                throw new InvalidOperationException(
                    $"Лист {group.Key}: лицо и оборот не образуют однозначную пару. Печать остановлена.");
            plan.Add(new Sheet(group.Key, faces[0], backs[0]));
        }
        return plan;
    }
}
