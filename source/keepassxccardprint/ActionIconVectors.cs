using Avalonia.Layout;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace KeePassXCCardPrint;

/// <summary>Single Fluent Regular icon factory for code-built UI actions.</summary>
internal static class ActionIconVectors
{
    internal static FluentIcon Create(string action, double size = 16) => Create(action switch
    {
        "print" => Icon.Print,
        "page" => Icon.DocumentOnePage,
        "settings" => Icon.Settings,
        "reset" => Icon.ArrowSync,
        "save" => Icon.Save,
        "check" => Icon.Checkmark,
        "cancel" => Icon.DismissCircle,
        "close" => Icon.Dismiss,
        "warning" => Icon.Warning,
        "exit" => Icon.SignOut,
        "history" => Icon.History,
        "rename" => Icon.Rename,
        _ => Icon.DocumentOnePage
    }, size);

    internal static FluentIcon Create(Icon icon, double size = 16) => new()
    {
        Icon = icon,
        IconVariant = IconVariant.Regular,
        IconSize = IconSize.Size20,
        Width = size,
        Height = size,
        FontSize = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
}
