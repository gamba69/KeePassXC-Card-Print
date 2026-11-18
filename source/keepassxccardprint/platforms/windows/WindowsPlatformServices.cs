using System.Runtime.Versioning;
using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.Platforms.Windows.Input;
using KeePassXCCardPrint.Platforms.Windows.Printing;
using KeePassXCCardPrint.Platforms.Windows.Security;
using KeePassXCCardPrint.Platforms.Windows.Storage;

namespace KeePassXCCardPrint.Platforms.Windows;

/// <summary>Windows is the only runtime host in this release; the shared contracts have no Win32 dependencies.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsPlatformServices : IPlatformServices
{
    public string PlatformLabel => "Windows";
    public IReadOnlyList<PlatformDependency> PlatformDependencies { get; } =
    [
        new("Avalonia.Win32", AboutDependencyVersions.AvaloniaWin32),
        new("SkiaSharp NativeAssets.Win32", AboutDependencyVersions.SkiaAssets),
        new("HarfBuzzSharp NativeAssets.Win32", AboutDependencyVersions.HarfBuzzAssets)
    ];
    public string ResourceDirectory => AppContext.BaseDirectory;
    public WindowFrameInsets WindowFrame => new(24, 64);
    public StringComparison FileNameComparison => StringComparison.OrdinalIgnoreCase;
    // Canonical Windows path identity for per-database selections.
    public string NormalizeDatabaseIdentityPath(string path)
        => Path.GetFullPath(path).Replace('/', '\\').ToLowerInvariant();
    public string ResolveSystemFontFamily(string family) => family;
    public string SystemFontOptions => "Segoe UI,Arial,Calibri,Tahoma,Verdana,Trebuchet MS,Atkinson Hyperlegible Next,Recursive";
    public string FallbackTextFont => "Segoe UI";
    public string FallbackSecretFont => "Consolas";
    public IPrinterService Printing { get; } = new WindowsPrinterService();
    public IConfigurationEnvironment Configuration { get; } = new WindowsConfigurationEnvironment();
    public ISecurityKeyService SecurityKeys { get; } = new WindowsSecurityKeyService();
    public ICloudPlatformServices Cloud { get; } = new WindowsCloudPlatformServices();
    public IPasswordInputLanguageScope CreatePasswordInputLanguageScope() => new WindowsPasswordInputLanguage();
}
