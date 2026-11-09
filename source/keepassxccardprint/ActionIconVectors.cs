using Avalonia.Controls;
using Avalonia.Media;

namespace KeePassXCCardPrint;

/// <summary>Small theme-inheriting vector action glyphs used in code-built modal buttons.
/// No icon fonts, bitmap assets or platform-specific file lookups are required.</summary>
internal static class ActionIconVectors
{
    // Fluent System Icons Dismiss Circle Regular 20 (MIT). Keep in sync with
    // the KpCancelIconGeometry resource used by AXAML dialogs.
    internal const string CancelGeometry = "M10 2C14.4183 2 18 5.58172 18 10C18 14.4183 14.4183 18 10 18C5.58172 18 2 14.4183 2 10C2 5.58172 5.58172 2 10 2ZM10 3C6.13401 3 3 6.13401 3 10C3 13.866 6.13401 17 10 17C13.866 17 17 13.866 17 10C17 6.13401 13.866 3 10 3ZM7.80943 7.11372L7.87868 7.17157L10 9.29289L12.1213 7.17157C12.2949 6.99801 12.5643 6.97872 12.7592 7.11372L12.8284 7.17157C13.002 7.34514 13.0213 7.61456 12.8863 7.80943L12.8284 7.87868L10.7071 10L12.8284 12.1213C13.002 12.2949 13.0213 12.5643 12.8863 12.7592L12.8284 12.8284C12.6549 13.002 12.3854 13.0213 12.1906 12.8863L12.1213 12.8284L10 10.7071L7.87868 12.8284C7.70511 13.002 7.43569 13.0213 7.24082 12.8863L7.17157 12.8284C6.99801 12.6549 6.97872 12.3854 7.11372 12.1906L7.17157 12.1213L9.29289 10L7.17157 7.87868C6.99801 7.70511 6.97872 7.43569 7.11372 7.24082L7.17157 7.17157C7.34514 6.99801 7.61456 6.97872 7.80943 7.11372Z";

    internal static PathIcon Create(string action)
    {
        // All glyphs are defined on a single 20x20 coordinate grid.
        string geometry=action switch
        {
            "print" => "M4 2H16V7H4ZM3 8H17V15H14V18H6V15H3ZM7 12V17H13V12Z",
            "page" => "M4 1H12L16 5V19H4ZM11 2V6H15ZM6 9H14V10H6ZM6 12H14V13H6Z",
            "settings" => "M8 2H12V4L14 5L16 4L18 7L16 9V11L18 13L16 16L14 15L12 16V18H8V16L6 15L4 16L2 13L4 11V9L2 7L4 4L6 5L8 4ZM10 7A3 3 0 1 0 10 13A3 3 0 1 0 10 7Z",
            "reset" => "M3 10A7 7 0 1 1 6 16L7 14A5 5 0 1 0 5 9H8V11H2V5H4V8Z",
            "save" => "M3 2H15L18 5V18H2V2ZM6 3V8H14V3ZM5 11H15V17H5Z",
            "check" => "M2 10L7 15L18 4L16 2L7 11L4 8Z",
            "cancel" => CancelGeometry,
            "close" => "M4 4L16 16L17 15L5 3ZM16 4L4 16L5 17L17 5Z",
            "warning" => "M10 2L19 18H1ZM10 6V12H11V6ZM10 14V16H11V14Z",
            "exit" => "M3 3H11V5H5V15H11V17H3ZM12 6L17 10L12 14V11H7V9H12Z",
            "history" => "M10 2A8 8 0 1 1 3 6V2H5V8H0V6H2A10 10 0 1 0 10 0ZM9 4H11V10L15 12L14 14L9 11Z",
            _ => "M4 1H12L16 5V19H4ZM11 2V6H15ZM6 9H14V10H6Z"
        };
        return new PathIcon
        {
            Data=StreamGeometry.Parse(geometry),Width=14,Height=14,
            VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center
        };
    }
}
