using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using KeePassXCCardPrint.ViewModels;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        ActualThemeVariantChanged += (_, _) => UpdateAppIcon();
        AttachedToVisualTree += (_, _) => UpdateAppIcon();
        LayoutUpdated += (_, _) => UpdateAppIcon();
    }

    private void UpdateAppIcon()
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        BrandArtwork.Apply(AboutAppIcon, ActualThemeVariant == ThemeVariant.Dark, scaling, 32);
    }

    // Keep technical details comfortably readable on a narrow left pane.
    // No controls are recreated; changing layout preserves diagnostics and clipboard state.
    private bool _singleColumn;

    private void OnPageSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 850;
        if (compact == _singleColumn) return;
        _singleColumn = compact;

        DetailsGrid.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "1.15*,*");
        DetailsGrid.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto" : "Auto");
        Grid.SetColumn(TechColumn, compact ? 0 : 1);
        Grid.SetRow(TechColumn, compact ? 1 : 0);
    }

    private async void OnCopyDiagnostics(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AboutViewModel vm) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) { vm.SetCopyResult(false); return; }
        try
        {
            await clipboard.SetTextAsync(vm.SafeDiagnosticSummary());
            vm.SetCopyResult(true);
        }
        catch (Exception) { vm.SetCopyResult(false); }
    }
}
