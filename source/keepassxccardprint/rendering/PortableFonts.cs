using KeePassXCCardPrint.Platforms;
using System.IO;
using SkiaSharp;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.Rendering;

/// <summary>
/// Portable specialist fonts for secret values and optional display text.
/// One verified variable TTF per family; Recursive and Recursive Mono share a file
/// but use distinct MONO-axis coordinates. System UI fonts are never downloaded.
/// </summary>
internal static class PortableFonts
{
    internal static readonly string[] SecretFamilies =
    [
        "Atkinson Hyperlegible Mono", "JetBrains Mono NL", "Recursive Mono",
        "IBM Plex Mono", "Source Code Pro", "Geist Mono", "Cascadia Mono"
    ];

    // The order of this list is shared with the PowerShell font manifest.
    // The same Recursive file is used for both plain text and monospaced secrets.
    internal static readonly string[] ReleaseFiles =
    [
        "AtkinsonHyperlegibleMono-Variable.ttf",
        "AtkinsonHyperlegibleNext-Variable.ttf",
        "JetBrainsMono-Variable.ttf",
        "Recursive-Variable.ttf",
        "IBMPlexMono-Variable.ttf",
        "SourceCodePro-Variable.ttf",
        "GeistMono-Variable.ttf",
        "CascadiaMono-Variable.ttf"
    ];

    internal static bool IsSecretFont(string? family) =>
        family is "Atkinson Hyperlegible Mono" or "JetBrains Mono NL" or
            "Recursive Mono" or "IBM Plex Mono" or "Source Code Pro" or "Geist Mono" or "Cascadia Mono";

    internal static bool IsPortableFont(string? family) =>
        IsSecretFont(family) || family is "Atkinson Hyperlegible Next" or "Recursive";

    private static string FileName(string family) => family switch
    {
        "Atkinson Hyperlegible Mono" => ReleaseFiles[0],
        "Atkinson Hyperlegible Next" => ReleaseFiles[1],
        "JetBrains Mono NL" => ReleaseFiles[2],
        "Recursive" or "Recursive Mono" => ReleaseFiles[3],
        "IBM Plex Mono" => ReleaseFiles[4],
        "Source Code Pro" => ReleaseFiles[5],
        "Geist Mono" => ReleaseFiles[6],
        "Cascadia Mono" => ReleaseFiles[7],
        _ => throw new ArgumentException("Unknown portable font family.", nameof(family))
    };

    private static string PathFor(string family) =>
        Path.Combine(PlatformServices.Current.ResourceDirectory, "fonts", FileName(family));

    internal static void RequireSecretFont(string family)
    {
        if (!IsSecretFont(family))
            throw new InvalidOperationException("Secret values require a bundled monospaced font.");
        var path = PathFor(family);
        if (!File.Exists(path))
            throw new FileNotFoundException("Portable monospaced font is missing. Run build.bat.", path);
    }

    internal static SKTypeface OpenTypeface(string family, int requestedWeight)
    {
        var path = PathFor(family);
        if (!File.Exists(path))
            throw new FileNotFoundException("Portable font face is missing.", path);
        using var baseFace = SKTypeface.FromFile(path)
            ?? throw new InvalidDataException("Portable font face is damaged: " + Path.GetFileName(path));

        // Clamp to ACTUAL ranges, never request unsupported styles or simulate bold.
        // IBM Plex Mono Var 1.0 supports wght=100..700, whereas Recursive
        // supports 300..1000. The user-facing editor currently offers 200..800.
        int weight = family switch
        {
            "IBM Plex Mono" => Math.Clamp(requestedWeight, 100, 700),
            "Cascadia Mono" => Math.Clamp(requestedWeight, 200, 700),
            "Recursive" or "Recursive Mono" => Math.Clamp(requestedWeight, 300, 1000),
            "Source Code Pro" => Math.Clamp(requestedWeight, 200, 900),
            "Geist Mono" => Math.Clamp(requestedWeight, 100, 900),
            _ => Math.Clamp(requestedWeight, 200, 800)
        };
        var axes = new List<SKFontVariationPositionCoordinate>
        {
            new() { Axis = SKFourByteTag.Parse("wght"), Value = weight }
        };
        if (family is "Recursive" or "Recursive Mono")
        {
            axes.Add(new() { Axis = SKFourByteTag.Parse("MONO"), Value = family == "Recursive Mono" ? 1 : 0 });
            axes.Add(new() { Axis = SKFourByteTag.Parse("CASL"), Value = 0 });
            axes.Add(new() { Axis = SKFourByteTag.Parse("CRSV"), Value = 0 });
            axes.Add(new() { Axis = SKFourByteTag.Parse("slnt"), Value = 0 });
        }
        var arguments = new SKFontArguments { VariationDesignPosition = axes.ToArray() };
        return baseFace.Clone(arguments)
            ?? throw new InvalidDataException("Variable-font axis selection failed: " + family);
    }
}
