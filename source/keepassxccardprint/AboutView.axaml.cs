using Avalonia.Controls;
using Avalonia.Input;
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
        InitializeUpdates();
        ActualThemeVariantChanged += (_, _) => UpdateAppIcon();
        AttachedToVisualTree += (_, _) => UpdateAppIcon();
        LayoutUpdated += (_, _) => UpdateAppIcon();
    }

    private void UpdateAppIcon()
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        BrandArtwork.Apply(AboutAppIcon, ActualThemeVariant == ThemeVariant.Dark, scaling, 32);
    }

    // The update and performance strips always span the page. Only their contents
    // and the original two-column technical details change layout at narrow widths.
    private bool _singleColumn;
    private bool _compactPerformance;
    private bool _compactPerformanceHeader;
    private bool _compactUpdates;

    private void OnPageSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var compactDetails = e.NewSize.Width < 850;
        if (compactDetails != _singleColumn)
        {
            _singleColumn = compactDetails;
            DetailsGrid.ColumnDefinitions = new ColumnDefinitions(compactDetails ? "*" : "1.15*,*");
            DetailsGrid.RowDefinitions = new RowDefinitions(compactDetails ? "Auto,Auto" : "Auto");
            Grid.SetColumn(TechColumn, compactDetails ? 0 : 1);
            Grid.SetRow(TechColumn, compactDetails ? 1 : 0);
        }

        // Four metrics use one horizontal line whenever the pane can hold them.
        // Only very narrow windows use a 2x2 metrics layout.
        var compactPerformance = e.NewSize.Width < 680;
        if (compactPerformance != _compactPerformance)
        {
            _compactPerformance = compactPerformance;
            PerformanceMetricsGrid.ColumnDefinitions = new ColumnDefinitions(compactPerformance ? "*,*" : "*,*,*,*");
            PerformanceMetricsGrid.RowDefinitions = new RowDefinitions(compactPerformance ? "Auto,Auto" : "Auto");
            Grid.SetRow(CacheMetric, compactPerformance ? 1 : 0);
            Grid.SetColumn(CacheMetric, compactPerformance ? 0 : 2);
            Grid.SetRow(MemoryMetric, compactPerformance ? 1 : 0);
            Grid.SetColumn(MemoryMetric, compactPerformance ? 1 : 3);
        }

        // Keep the checkbox and copy button aligned with the title unless the
        // available width requires a dedicated header row for both actions.
        var compactPerformanceHeader = e.NewSize.Width < 1000;
        if (compactPerformanceHeader != _compactPerformanceHeader)
        {
            _compactPerformanceHeader = compactPerformanceHeader;
            PerformanceHeaderGrid.RowDefinitions = new RowDefinitions(compactPerformanceHeader ? "Auto,Auto" : "Auto");
            Grid.SetRow(PerformanceActions, compactPerformanceHeader ? 1 : 0);
            Grid.SetColumn(PerformanceActions, compactPerformanceHeader ? 0 : 2);
            Grid.SetColumnSpan(PerformanceActions, compactPerformanceHeader ? 3 : 1);
            PerformanceActions.HorizontalAlignment = compactPerformanceHeader
                ? Avalonia.Layout.HorizontalAlignment.Left
                : Avalonia.Layout.HorizontalAlignment.Right;
        }
        PerformanceActions.Orientation = compactPerformance
            ? Avalonia.Layout.Orientation.Vertical
            : Avalonia.Layout.Orientation.Horizontal;

        var compactUpdates = e.NewSize.Width < 680;
        if (compactUpdates != _compactUpdates)
        {
            _compactUpdates = compactUpdates;
            UpdateLayoutGrid.ColumnDefinitions = new ColumnDefinitions(compactUpdates ? "*" : "*,Auto");
            UpdateLayoutGrid.RowDefinitions = new RowDefinitions(compactUpdates ? "Auto,Auto,Auto" : "Auto,Auto");
            Grid.SetColumn(UpdateActions, compactUpdates ? 0 : 1);
            Grid.SetRow(UpdateActions, compactUpdates ? 2 : 0);
            Grid.SetRowSpan(UpdateActions, compactUpdates ? 1 : 2);
        }
    }

    private async void OnRepositoryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control link || !e.GetCurrentPoint(link).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        await OpenRepositoryAsync();
    }

    private async void OnRepositoryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space)) return;
        e.Handled = true;
        await OpenRepositoryAsync();
    }

    private async Task OpenRepositoryAsync()
    {
        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is not null &&
                await launcher.LaunchUriAsync(new Uri(AboutViewModel.RepositoryAddress)))
                return;
        }
        catch (Exception) { /* The OS may have no URI handler; keep the link copyable. */ }
        if (DataContext is AboutViewModel vm) vm.SetRepositoryOpenFailed();
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
