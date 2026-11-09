using System.Security.Cryptography;
using KeePassXCCardPrint.Platforms;

namespace KeePassXCCardPrint.Services;

/// <summary>Safe config-only transfer. Database files, passwords, and key files
/// are never copied. The source remains intact. Existing target files are
/// backed up and restored if any stage of the transfer fails.</summary>
public static class ConfigurationTransfer
{
    private static bool SafeFile(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    public static void CopyCurrent(string sourceRoot, string destinationRoot)
    {
        if (string.Equals(Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar),
                          Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar),
                          PlatformServices.Current.FileNameComparison)) return;
        if (!Directory.Exists(sourceRoot)) return;

        var entries = new List<(string Source, string Relative)>();
        foreach (var name in new[]
        {
            "ui-shell.json", "vault-state.json", "appearance.json", "mapping.json",
            "domains.json", "appearance-template-source.json"
        })
        {
            var path = Path.Combine(sourceRoot, name);
            if (File.Exists(path) && SafeFile(path)) entries.Add((path, name));
        }
        foreach (var name in new[] { "templates", "print-tracking" })
        {
            var folder = Path.Combine(sourceRoot, name);
            if (!Directory.Exists(folder) || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (var path in Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
                if (SafeFile(path)) entries.Add((path, Path.Combine(name, Path.GetFileName(path))));
        }

        Directory.CreateDirectory(destinationRoot);
        var transaction = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(destinationRoot, ".storage-stage-" + transaction);
        var backup = Path.Combine(destinationRoot, "backups", "storage-switch-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + transaction[..6]);
        Directory.CreateDirectory(staging);
        var changed = new List<(string Destination, bool Existed)>();
        try
        {
            // Stage every source file and verify its byte identity BEFORE replacing anything.
            foreach (var (source, relative) in entries)
            {
                var destination = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, false);
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(File.ReadAllBytes(source)),
                                                              SHA256.HashData(File.ReadAllBytes(destination))))
                    throw new IOException("Configuration transfer verification failed.");
            }
            foreach (var (_, relative) in entries)
            {
                var target = Path.Combine(destinationRoot, relative);
                var staged = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var existed = File.Exists(target);
                if (existed)
                {
                    if (!SafeFile(target)) throw new IOException("Unsafe configuration destination.");
                    var saved = Path.Combine(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Copy(target, saved, false);
                }
                // A failed move leaves the prior destination untouched.
                File.Move(staged, target, true);
                changed.Add((target, existed));
            }
        }
        catch
        {
            for (var i = changed.Count - 1; i >= 0; --i)
            {
                var (target, existed) = changed[i];
                if (existed)
                {
                    var relative = Path.GetRelativePath(destinationRoot, target);
                    File.Copy(Path.Combine(backup, relative), target, true);
                }
                else if (File.Exists(target)) File.Delete(target);
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
        // Keep a backup ONLY when there actually were overwritten target files.
        if (Directory.Exists(backup) && !Directory.EnumerateFiles(backup, "*", SearchOption.AllDirectories).Any())
            Directory.Delete(backup, true);
    }
}
