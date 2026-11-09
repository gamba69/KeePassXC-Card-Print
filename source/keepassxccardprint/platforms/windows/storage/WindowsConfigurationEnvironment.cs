using System.Runtime.Versioning;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.Platforms.Windows.Storage;

/// <summary>Windows configuration roots and the portable/system mode selector.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsConfigurationEnvironment : IConfigurationEnvironment
{
    // The selector is not part of the portable data folder; on an unknown PC,
    // an existing portable data folder takes precedence when no selector exists.
    private string ModeFile => Path.Combine(AppDataRoot, "storage-mode.txt");
    public string AppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "keepassxccardprint");
    public string LocalRoot => Path.Combine(ProgramFolder(), "data");

    private static string ProgramFolder()
    {
        var exe = new DirectoryInfo(AppContext.BaseDirectory);
        // For development runs, keep portable settings in repository data/,
        // outside the generated publish/ folder which is cleaned on rebuild.
        // An extracted release remains fully portable beside its executable.
        var sourceRoot = exe.Parent;
        if (exe.Name.Equals("publish", StringComparison.OrdinalIgnoreCase) &&
            sourceRoot is not null &&
            File.Exists(Path.Combine(sourceRoot.FullName, "build.bat")) &&
            File.Exists(Path.Combine(sourceRoot.FullName, "source", "keepassxccardprint", "KeePassXCCardPrint.csproj")))
            return sourceRoot.FullName;
        return exe.FullName;
    }

    public ConfigurationStorage ReadMode()
    {
        try
        {
            if (File.Exists(ModeFile) && new FileInfo(ModeFile).Length <= 32)
            {
                var mode = File.ReadAllText(ModeFile).Trim();
                if (mode == "local") return ConfigurationStorage.Local;
                if (mode == "appdata") return ConfigurationStorage.AppData;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return Directory.Exists(LocalRoot) ? ConfigurationStorage.Local : ConfigurationStorage.AppData;
    }

    public void Activate(ConfigurationStorage mode)
    {
        Directory.CreateDirectory(AppDataRoot);
        var temp = ModeFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, mode == ConfigurationStorage.Local ? "local" : "appdata");
            File.Move(temp, ModeFile, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
