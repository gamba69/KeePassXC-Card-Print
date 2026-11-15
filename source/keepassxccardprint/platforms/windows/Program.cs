using Avalonia;
using KeePassXCCardPrint.Rendering;
using KeePassXCCardPrint.ViewModels;
using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.Platforms;
using KeePassXCCardPrint.Platforms.Windows;
using KeePassXCCardPrint.Platforms.Windows.Updates;

namespace KeePassXCCardPrint;

internal static class Program
{

    [STAThread]
    public static void Main(string[] args)
    {
        // The update helper runs without Avalonia or opening a database.
        if(args.Length>0 && args[0]=="--apply-update")
        {
            Environment.ExitCode=WindowsUpdateService.ApplyUpdate(args);
            return;
        }
        if(args.Length>0 && args[0]=="--recover-update")
        {
            Environment.ExitCode=WindowsUpdateService.ApplyRecovery(args);
            return;
        }
        // The new EXE must be smoke-testable during an active update journal.
        if(args.Contains("--selftest-update",StringComparer.Ordinal))
        {
            Environment.ExitCode=0;
            return;
        }
        // A failed/interrupted install is recovered using the previously
        // copied, known-good helper before normal application initialization.
        if(WindowsUpdateService.LaunchInterruptedRecovery(args))return;
        UpdaterHost.ReinstallRequested=args.Contains("--reinstall",StringComparer.Ordinal);
        WindowsHost.Configure();
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
        AppStartupState.ResetWindowPlacement = args.Contains("--reset-window", StringComparer.Ordinal);
        WindowsHost.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

}
