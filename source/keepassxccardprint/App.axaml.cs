using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using KeePassXCCardPrint.Platforms.Windows;
using KeePassXCCardPrint.Platforms.Windows.Updates;

namespace KeePassXCCardPrint;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DialogScreenPolicy.Install(desktop);
            // Both window types derive from Avalonia.Controls.Window. Explicitly
            // use the common type rather than an untyped conditional expression.
            Avalonia.Controls.Window mainWindow;
            if (UpdateHelperBootstrap.Arguments is { } args)
                mainWindow = UpdateOperationWindow.Worker(args);
            else
                mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            // Helper windows never own the application-wide single-instance gate.
            if (UpdateHelperBootstrap.Arguments is null)
                WindowsSingleInstance.Attach(mainWindow);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
