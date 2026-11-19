using System.Text;

namespace KeePassXCCardPrint.Services;

/// <summary>
/// Local-only failure log for sporadic background/UI cloud faults. Never includes
/// exception messages, URLs, request headers, paths, OAuth codes or tokens.
/// </summary>
public static class CloudSyncDiagnostics
{
    private static readonly object Gate = new();
    private const long MaxBytes = 256 * 1024;
    public static string LogPath => Path.Combine(SettingsLocation.AppDataRoot, "cloud-sync-errors.log");

    public static void Record(string operation, Exception exception)
    {
        try
        {
            // Call sites use fixed internal operation names. Defend the log itself
            // against accidental free-form input containing user information.
            if (operation.Length > 48 || operation.Any(c => !char.IsAsciiLetterLower(c) &&
                !char.IsAsciiLetterUpper(c) && c is not ('-' or '_' or '.')))
                operation = "cloud-operation";
            var builder = new StringBuilder()
                .Append(DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" | ").Append(operation)
                .Append(" | ").Append(exception.GetType().Name)
                .Append(" | HRESULT=0x")
                .Append(exception.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
            // Stack frame names are useful in an AOT crash report; do not copy
            // exception.Message or any `in C:\\path:line ...` source suffix.
            foreach (var frame in (exception.StackTrace ?? "").Split('\n').Take(16))
            {
                var methodOnly = frame.Split(" in ", 2, StringSplitOptions.None)[0].Trim();
                if (methodOnly.Length > 0 && methodOnly.Length <= 512)
                    builder.Append("\n  ").Append(methodOnly);
            }
            builder.AppendLine();
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsLocation.AppDataRoot);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxBytes)
                    File.Move(LogPath, LogPath + ".previous", true);
                File.AppendAllText(LogPath, builder.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception) { /* Diagnostics must never turn an error into a crash. */ }
    }
}
