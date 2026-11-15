namespace KeePassXCCardPrint.Platforms.Windows.Updates;

// Helper-only process state. Keep it separate from UpdaterHost, whose static
// initializer creates the normal application's HTTP update service.
internal static class UpdateHelperBootstrap
{
    internal static string[]? Arguments;
    internal static bool WorkStarted;
}
