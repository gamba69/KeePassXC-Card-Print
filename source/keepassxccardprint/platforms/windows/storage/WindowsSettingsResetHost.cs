using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.Platforms.Windows.Storage;

/// <summary>Runs a destructive, user-approved reset only after the original GUI
/// process has fully exited and released its file watchers and save handlers.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsSettingsResetHost
{
    internal static void Start(SettingsResetOptions options)
    {
        var executable = Environment.ProcessPath ?? throw new IOException("Application path unavailable.");
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        info.ArgumentList.Add("--reset-settings-worker");
        info.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        info.ArgumentList.Add(options.AllStorages ? "all" : "current");
        info.ArgumentList.Add(options.ClearPrintHistory ? "clear-history" : "keep-history");
        using var started = Process.Start(info);
        if (started is null) throw new IOException("Could not start the settings reset worker.");
    }

    internal static int Run(string[] args)
    {
        if (args.Length != 4 || args[0] != "--reset-settings-worker" ||
            !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parentId) ||
            parentId <= 0 || args[2] is not ("all" or "current") ||
            args[3] is not ("clear-history" or "keep-history")) return 2;
        try
        {
            try
            {
                using var parent = Process.GetProcessById(parentId);
                if (!parent.WaitForExit(120_000))
                    throw new TimeoutException("The original Card Print process is still running; no settings were reset.");
            }
            catch (ArgumentException) { /* Original process already exited. */ }
            WindowsHost.Configure();
            var options = new SettingsResetOptions(args[2] == "all", args[3] == "clear-history");
            var result = SettingsResetService.Execute(options);
            SettingsResetService.StoreOutcome(true, result.BackupDirectory);
        }
        catch (Exception ex)
        {
            // The failure is reported after reopening the UI; never include
            // configuration contents or cloud credentials in the result.
            try { SettingsResetService.StoreOutcome(false, ex.GetType().Name); }
            catch (Exception) { }
        }
        try
        {
            var executable = Environment.ProcessPath ?? throw new IOException("Application path unavailable.");
            Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            });
        }
        catch (Exception) { return 3; }
        return 0;
    }
}
