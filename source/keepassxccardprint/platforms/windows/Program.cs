using Avalonia;
using KeePassXCCardPrint.Rendering;
using KeePassXCCardPrint.ViewModels;
using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.Platforms;
using KeePassXCCardPrint.Platforms.Windows;
using KeePassXCCardPrint.Platforms.Windows.Updates;
using KeePassXCCardPrint.Platforms.Windows.Storage;

namespace KeePassXCCardPrint;

internal static class Program
{
    // Last-resort local diagnostics for intermittent managed crashes. Logging
    // does not suppress a fatal exception and never records exception messages.
    private static void InstallFailureDiagnostics()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception error)
                CloudSyncDiagnostics.Record("unhandled-managed-exception", error);
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            CloudSyncDiagnostics.Record("unobserved-task", eventArgs.Exception);
            eventArgs.SetObserved();
        };
    }

    [STAThread]
    public static void Main(string[] args)
    {
        InstallFailureDiagnostics();
        if (args.Length > 0 && args[0] == "--reset-settings-worker")
        {
            if (!OperatingSystem.IsWindows())
            {
                Environment.ExitCode = 1;
                return;
            }
            Environment.ExitCode = WindowsSettingsResetHost.Run(args);
            return;
        }
        // The temporary helper uses the same Native AOT/Avalonia runtime but
        // instantiates only its progress window, never the database UI.
        if(args.Length>0 && (args[0] is "--apply-update" or "--recover-update"))
        {
            UpdateHelperBootstrap.Arguments=args;
            try
            {
                WindowsHost.Configure();
                WindowsHost.BuildAvaloniaApp().StartWithClassicDesktopLifetime(Array.Empty<string>());
            }
            catch(Exception)
            {
                // If Avalonia cannot initialize, complete the same filesystem
                // transaction headlessly. Never retry once the worker started.
                if(!UpdateHelperBootstrap.WorkStarted)
                    Environment.ExitCode=args[0]=="--apply-update"
                        ?WindowsUpdateService.ApplyUpdate(args)
                        :WindowsUpdateService.ApplyRecovery(args);
                else Environment.ExitCode=1;
            }
            return;
        }
        // The new EXE must be smoke-testable during an active update journal.
        if(args.Contains("--selftest-update",StringComparer.Ordinal))
        {
            Environment.ExitCode=0;
            return;
        }
        if (args.Contains("--self-test-cloud", StringComparer.Ordinal))
        {
            int status = CloudArchiveSelfTests.Run();
            int index = Array.IndexOf(args, "--self-test-cloud");
            if (index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                File.WriteAllText(args[index + 1], CloudArchiveSelfTests.Report);
            Environment.ExitCode = status;
            return;
        }
        // A failed/interrupted install is recovered using the previously
        // copied, known-good helper before normal application initialization.
        if(WindowsUpdateService.LaunchInterruptedRecovery(args))return;
        UpdaterHost.ReinstallRequested=args.Contains("--reinstall",StringComparer.Ordinal);
        WindowsHost.Configure();
        if (args.Contains("--self-test-scanner", StringComparer.Ordinal))
        {
            int status = ScannerSelfTests.Run();
            int index = Array.IndexOf(args, "--self-test-scanner");
            if (index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                File.WriteAllText(args[index + 1], ScannerSelfTests.Report);
            Environment.ExitCode = status;
            return;
        }
        if(args.Contains("--self-test-updates",StringComparer.Ordinal))
        {
            int status=UpdateSelfTests.Run();
            int index=Array.IndexOf(args,"--self-test-updates");
            if(index>=0 && index+1<args.Length && !args[index+1].StartsWith("--",StringComparison.Ordinal))
                File.WriteAllText(args[index+1],UpdateSelfTests.Report);
            Environment.ExitCode=status;
            return;
        }
        if (args.Contains("--self-test-mapping",StringComparer.Ordinal))
        {
            int status=MappingSelfTests.Run();
            int index=Array.IndexOf(args,"--self-test-mapping");
            if(index>=0 && index+1<args.Length && !args[index+1].StartsWith("--",StringComparison.Ordinal))
                File.WriteAllText(args[index+1],MappingSelfTests.Report);
            Environment.ExitCode=status;
            return;
        }
        if (args.Contains("--self-test-vault-errors", StringComparer.Ordinal))
        {
            // Run in the published Native AOT binary without opening any vault.
            int status = VaultErrorSelfTests.Run();
            int index = Array.IndexOf(args, "--self-test-vault-errors");
            if (index >= 0 && index + 1 < args.Length &&
                !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                File.WriteAllText(args[index + 1], VaultErrorSelfTests.Report);
            Environment.ExitCode = status;
            return;
        }
        if (args.Contains("--self-test-xps", StringComparer.Ordinal))
        {
            int status=0;
            string report="[PASS] Windows XPS OM: vector FixedPage parsed without print spooler.";
            try
            {
                PlatformServices.Current.Printing.ValidateVectorBackend();
            }
            catch(Exception ex)
            {
                status=9;
                report="[FAIL] Windows XPS OM: "+ex.GetType().Name+": "+ex.Message;
            }
            int index=Array.IndexOf(args,"--self-test-xps");
            if(index>=0 && index+1<args.Length && !args[index+1].StartsWith("--",StringComparison.Ordinal))
                File.WriteAllText(args[index+1],report);
            Environment.ExitCode=status;
            return;
        }
        if (args.Contains("--self-test-layout", StringComparer.Ordinal))
        {
            // Test the Avalonia 12 + SkiaSharp 4 + HarfBuzz 14 bootstrap, not
            // only the separate card bitmap renderer. Setup has no main window.
            int status;
            string report;
            try
            {
                WindowsHost.BuildAvaloniaApp().SetupWithoutStarting();
                status = CardEngineSelfTests.Run();
                report = CardEngineSelfTests.Report;
            }
            catch (Exception ex)
            {
                status = 8;
                report = "[FAIL] graphics bootstrap: " + ex.GetType().Name + ": " + ex.Message;
            }
            int index = Array.IndexOf(args, "--self-test-layout");
            if (index >= 0 && index + 1 < args.Length &&
                !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                File.WriteAllText(args[index + 1], report);
            Environment.ExitCode = status;
            return;
        }
        // Only the interactive UI is single-instance. Update/reset workers and
        // self-tests above must remain independently executable.
        WindowsSingleInstance? primaryInstance;
        try { primaryInstance = WindowsSingleInstance.EnterOrActivate(); }
        catch (Exception ex)
        {
            try { CloudSyncDiagnostics.Record("single-instance-startup", ex); } catch { }
            Environment.ExitCode = 1;
            return; // Fail closed rather than create an unprotected second UI.
        }
        using (primaryInstance)
        {
            if (primaryInstance is null) return;
            try { ConfigurationMaintenance.CleanOnStartup(); }
            catch (Exception ex) { CloudSyncDiagnostics.Record("startup-storage-cleanup", ex); }
            AppStartupState.ResetWindowPlacement = args.Contains("--reset-window", StringComparer.Ordinal);
            WindowsHost.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
    }

}
