namespace KeePassXCCardPrint.Rendering;

/// <summary>
/// Sequential screen-preview navigation. A duplex physical card has two stops:
/// front, back. The next physical card starts only after its predecessor's back.
/// This class has no UI or KDBX dependencies and is covered by Native AOT self-tests.
/// </summary>
internal static class CardPreviewSequence
{
    internal static int Count(int physicalCards, bool duplex) =>
        checked(Math.Max(0, physicalCards) * (duplex ? 2 : 1));

    internal static int Position(int physicalIndex, bool back, bool duplex) =>
        duplex ? checked(physicalIndex * 2 + (back ? 1 : 0)) : physicalIndex;

    internal static (int PhysicalIndex, bool Back) At(int position, bool duplex) =>
        duplex ? (position / 2, position % 2 != 0) : (position, false);

    internal static bool CanStep(int physicalCards, int physicalIndex, bool back,
        bool duplex, int direction)
    {
        if (physicalCards <= 0 || (direction != -1 && direction != 1)) return false;
        int next = Position(physicalIndex, back, duplex) + direction;
        return next >= 0 && next < Count(physicalCards, duplex);
    }
}
