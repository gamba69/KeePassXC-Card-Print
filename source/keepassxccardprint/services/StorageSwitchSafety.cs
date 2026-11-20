namespace KeePassXCCardPrint.Services;

/// <summary>Boundaries for best-effort UI work after a storage transition.
/// The underlying storage operation remains responsible for reporting failure.</summary>
internal static class StorageSwitchSafety
{
    internal static void BestEffort(Action action, Action<Exception>? report = null)
    {
        try { action(); }
        catch (Exception error)
        {
            // Diagnostic output must never be another source of dispatcher crashes.
            if (report is not null)
                try { report(error); }
                catch (Exception) { }
        }
    }

    internal static void Notify(Action<string, bool>? listeners, string message,
        bool succeeded, Action<Exception>? report = null)
    {
        if (listeners is null) return;
        foreach (var callback in listeners.GetInvocationList())
            if (callback is Action<string, bool> listener)
                BestEffort(() => listener(message, succeeded), report);
    }
}
