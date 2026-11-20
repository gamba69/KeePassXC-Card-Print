using System.Text;

namespace KeePassXCCardPrint.Services;

/// <summary>Options must be explicitly confirmed by a user. False preserves print history.</summary>
public readonly record struct SettingsResetOptions(bool AllStorages, bool ClearPrintHistory);
public readonly record struct SettingsResetResult(string BackupDirectory, int FileCount);
public readonly record struct SettingsResetOutcome(bool Success, string Message);

/// <summary>Only known Card Print settings are reset. No vault, key file, remote archive,
/// updater binary, Proton CLI, or Windows Credential Manager entry is deleted.</summary>
public static class SettingsResetService
{
    internal static string BackupBase => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "keepassxccardprint-reset-backups");
    private static string OutcomePath => Path.Combine(BackupBase, "last-reset-result.txt");

    /// <summary>Called after UI shutdown; always resets all application storages.</summary>
    public static SettingsResetResult Execute(SettingsResetOptions options)
        => ConfigurationMaintenance.ResetAllAsync(options.ClearPrintHistory).GetAwaiter().GetResult();

    internal static void StoreOutcome(bool success, string message)
    {
        Directory.CreateDirectory(BackupBase);
        var temp = OutcomePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, (success ? "OK" : "ERROR") + "\n" + message, Encoding.UTF8);
            File.Move(temp, OutcomePath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static SettingsResetOutcome? ConsumeOutcome()
    {
        try
        {
            if (!File.Exists(OutcomePath) || new FileInfo(OutcomePath).Length > 4096) return null;
            var lines = File.ReadAllLines(OutcomePath);
            File.Delete(OutcomePath);
            if (lines.Length != 2 || lines[0] is not ("OK" or "ERROR")) return null;
            return new SettingsResetOutcome(lines[0] == "OK", lines[1]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

}
