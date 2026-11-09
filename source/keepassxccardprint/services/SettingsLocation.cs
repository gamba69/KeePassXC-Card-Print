using KeePassXCCardPrint.Platforms;

namespace KeePassXCCardPrint.Services;

public enum ConfigurationStorage { AppData, Local }

/// <summary>OS-independent location facade. The platform host owns persistence
/// of the mode selector; all stores continue using the same configuration roots.</summary>
public static class SettingsLocation
{
    private static ConfigurationStorage? _mode;
    public static ConfigurationStorage Mode => _mode ??= PlatformServices.Current.Configuration.ReadMode();
    public static string DirectoryPath => RootFor(Mode);
    public static string FilePath(string name) => Path.Combine(DirectoryPath, name);
    public static string AppDataRoot => PlatformServices.Current.Configuration.AppDataRoot;
    public static string LocalRoot => PlatformServices.Current.Configuration.LocalRoot;
    public static string RootFor(ConfigurationStorage mode) =>
        mode == ConfigurationStorage.Local ? LocalRoot : AppDataRoot;

    public static void Activate(ConfigurationStorage mode)
    {
        // Do not change the active storage location if persistence fails.
        PlatformServices.Current.Configuration.Activate(mode);
        _mode = mode;
    }
}
