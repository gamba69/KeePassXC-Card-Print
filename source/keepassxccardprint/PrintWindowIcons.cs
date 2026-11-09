using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace KeePassXCCardPrint;

/// <summary>Use the application's light titlebar icon on all secondary windows.</summary>
internal static class PrintWindowIcons
{
    internal static void Attach(Window window)
    {
        const string root = "avares://KeePassXCCardPrint/assets/";
        using var whiteStream = AssetLoader.Open(new Uri(root + "CardPrint_white.ico"));
        var white = new WindowIcon(whiteStream);

        void Apply() => window.Icon = white;
        window.Opened += (_, _) => Apply();
        Apply();
    }
}
