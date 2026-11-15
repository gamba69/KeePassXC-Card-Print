using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using KeePassXCCardPrint.Platforms.Windows.Updates;

namespace KeePassXCCardPrint;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = UpdateHelperBootstrap.Arguments is { } args
                ? UpdateOperationWindow.Worker(args)
                : new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
