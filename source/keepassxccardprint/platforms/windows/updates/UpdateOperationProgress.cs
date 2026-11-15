using System.Security.Cryptography;
using System.Text;

namespace KeePassXCCardPrint.Platforms.Windows.Updates;

// Progress is transport-neutral. Only percentage values obtained from file
// byte counts or completed file operations are determinate.
internal sealed record UpdateOperationProgress(string Phase, int? Percent = null, string Detail = "");

internal sealed record UpdateOperationResult(string Status, string Operation,
    string FromVersion, string ToVersion, string Detail = "");

// A result belongs to the exact portable installation, not to the user profile.
// It is deliberately separate from completed.txt and staging cleanup.
internal static class UpdateOperationResults
{
    internal static string PathFor(string root, string target)
    {
        var canonical = Path.GetFullPath(target).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return Path.Combine(root, "results", hash + ".txt");
    }

    internal static void Save(string root, string target, UpdateOperationResult result)
    {
        result = result with { Detail = Clean(result.Detail) };
        if (!Valid(result)) throw new InvalidDataException("Invalid update result.");
        var path = PathFor(root, target);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllLines(temp,
                new[] { result.Status, result.Operation, result.FromVersion,
                    result.ToVersion, Clean(result.Detail) }, new UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    internal static UpdateOperationResult? Read(string root, string target)
    {
        var path = PathFor(root, target);
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 2048 ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return null;
            var lines = File.ReadAllLines(path);
            if (lines.Length != 5) return null;
            var value = new UpdateOperationResult(lines[0], lines[1], lines[2], lines[3], lines[4]);
            return Valid(value) ? value : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { return null; }
    }

    // 7.0.x helpers wrote completed.txt but could not know about the 7.1
    // result journal. Capture that verified completion BEFORE cleanup so the
    // first migration to 7.1 also receives an acknowledgement. The previous
    // version is not available reliably, so show only the running version.
    internal static void CaptureLegacyCompleted(string root, string target, string runningVersion)
    {
        if (Read(root,target) is not null) return;
        try
        {
            var marker = Path.Combine(root,"completed.txt");
            if (!File.Exists(marker)) return;
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var stage = Path.GetFullPath(File.ReadAllText(marker).Trim());
            if (!stage.StartsWith(fullRoot,StringComparison.OrdinalIgnoreCase) ||
                (File.GetAttributes(stage) & FileAttributes.ReparsePoint) != 0) return;
            var stageTarget = Path.GetFullPath(File.ReadAllText(
                Path.Combine(stage,"target.txt")).Trim());
            if (!string.Equals(stageTarget,Path.GetFullPath(target),
                StringComparison.OrdinalIgnoreCase)) return;
            var staged = File.ReadAllText(Path.Combine(stage,"verified.txt")).Trim();
            if (CardPrintUpdatePolicy.ParseVersion(staged).ToString(3) != runningVersion) return;
            var pointer = Path.Combine(root,"ready.txt");
            var lines = File.Exists(pointer) ? File.ReadAllLines(pointer) : Array.Empty<string>();
            var operation = lines.Length==2 && lines[1]=="repair" ? "repair" : "update";
            Save(root,target,new("installed",operation,runningVersion,runningVersion));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidDataException or OverflowException)
        { /* Ignore interrupted or unrelated legacy installations. */ }
    }

    internal static void Consume(string root, string target)
    {
        try { File.Delete(PathFor(root, target)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static string Clean(string detail) =>
        new(detail.Where(c => !char.IsControl(c) && c != '\t').Take(500).ToArray());

    private static bool Valid(UpdateOperationResult result) =>
        (result.Status is "installed" or "rolled-back" or "failed") &&
        (result.Operation is "update" or "repair") &&
        Version.TryParse(result.FromVersion, out _) &&
        Version.TryParse(result.ToVersion, out _) &&
        result.Detail.Length <= 500 && !result.Detail.Any(c => c is '\r' or '\n');
}
