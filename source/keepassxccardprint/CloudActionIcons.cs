using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace KeePassXCCardPrint;

/// <summary>Use the same upstream Fluent glyphs as the rest of the main window.</summary>
internal static class CloudActionIcons
{
    internal static FluentIcon ForStage(string stage) => Create(stage switch
    {
        "Uploading" => Icon.CloudArrowUp,
        "Downloading" => Icon.CloudArrowDown,
        "Success" => Icon.CloudCheckmark,
        "Error" or "Conflict" => Icon.CloudError,
        _ => Icon.Cloud
    });

    internal static FluentIcon Create(Icon icon, double size = 15) => ActionIconVectors.Create(icon, size);
}
