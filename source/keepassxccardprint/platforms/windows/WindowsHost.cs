using Avalonia;
using KeePassXCCardPrint.Platforms;

namespace KeePassXCCardPrint.Platforms.Windows;

/// <summary>Windows-only composition root. Future hosts supply their own Avalonia backend and services.</summary>
internal static class WindowsHost
{
    internal static void Configure()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("This release contains only the Windows host.");
        PlatformServices.Initialize(new WindowsPlatformServices());
    }

    internal static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseWin32()
        .UseSkia()
        .UseHarfBuzz();
}
