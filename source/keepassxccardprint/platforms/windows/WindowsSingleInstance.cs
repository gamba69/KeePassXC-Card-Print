using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.Platforms.Windows;

/// <summary>
/// One interactive Card Print window per Windows logon session. A second launch
/// signals the first process and exits before touching configuration storage.
/// Helper/update/self-test processes deliberately bypass this coordinator.
/// </summary>
internal sealed class WindowsSingleInstance : IDisposable
{
    private const string MutexName = @"Local\KeePassXCCardPrint.MainWindow";
    private const string ActivateEventName = @"Local\KeePassXCCardPrint.ActivateMainWindow";
    private const int SwRestore = 9;
    private static WindowsSingleInstance? _current;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private readonly ManualResetEvent _stopEvent = new(false);
    private readonly Thread _listener;
    private Window? _window; // Only read and written on the Avalonia UI thread.
    private bool _activationPending;
    private int _disposed;

    private WindowsSingleInstance(Mutex mutex, EventWaitHandle activateEvent)
    {
        _mutex = mutex;
        _activateEvent = activateEvent;
        _listener = new Thread(Listen)
        {
            IsBackground = true,
            Name = "KeePassXC Card Print single-instance activation"
        };
    }

    internal static WindowsSingleInstance? EnterOrActivate()
    {
        // Initial ownership is held for the full desktop lifetime, not just
        // until the main window is created. The kernel releases it on a crash.
        var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            NotifyExisting();
            return null;
        }

        EventWaitHandle? activation = null;
        try
        {
            activation = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            var primary = new WindowsSingleInstance(mutex, activation);
            _current = primary;
            primary._listener.Start();
            return primary;
        }
        catch
        {
            _current = null;
            activation?.Dispose();
            mutex.ReleaseMutex();
            mutex.Dispose();
            throw;
        }
    }

    private static void NotifyExisting()
    {
        // EventWaitHandle.OpenExisting is Windows-only (CA1416). Keep the guard
        // in this method so the platform analyzer can prove it is safe.
        if (!OperatingSystem.IsWindows()) return;
        // The first launch can own its mutex before its activation event exists.
        // Allow a short startup race, but never start a second application UI.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ActivateEventName);
                signal.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(75);
            }
            catch (Exception ex)
            {
                SafeLog("single-instance-notify", ex);
                return;
            }
        }
    }

    internal static void Attach(Window window)
    {
        var primary = _current;
        if (primary is null) return;
        primary._window = window;
        window.Opened += (_, _) =>
        {
            if (primary._activationPending) primary.ActivateWindow();
        };
        if (primary._activationPending && window.IsVisible)
            primary.ActivateWindow();
    }

    private void Listen()
    {
        try
        {
            var handles = new WaitHandle[] { _activateEvent, _stopEvent };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                if (Volatile.Read(ref _disposed) != 0) break;
                try { Dispatcher.UIThread.Post(ActivateWindow); }
                catch (Exception ex) { SafeLog("single-instance-dispatch", ex); }
            }
        }
        catch (ObjectDisposedException) { /* Normal shutdown race. */ }
        catch (Exception ex) { SafeLog("single-instance-listener", ex); }
    }

    private void ActivateWindow()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (_window is null || !_window.IsVisible)
        {
            _activationPending = true;
            return;
        }
        _activationPending = false;
        try
        {
            var wasMinimized = _window.WindowState == WindowState.Minimized;
            var hwnd = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (wasMinimized)
            {
                // SW_RESTORE preserves Windows' previous maximized/normal placement.
                if (hwnd != IntPtr.Zero) ShowWindow(hwnd, SwRestore);
                else _window.WindowState = WindowState.Normal;
            }
            if (hwnd != IntPtr.Zero)
                SetForegroundWindow(hwnd); // Windows may restrict foreground stealing.
            _window.Activate();
        }
        catch (Exception ex) { SafeLog("single-instance-activate", ex); }
    }

    private static void SafeLog(string operation, Exception error)
    {
        try { CloudSyncDiagnostics.Record(operation, error); }
        catch { /* Diagnostics must not crash the main process. */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (ReferenceEquals(_current, this)) _current = null;
        _stopEvent.Set();
        _activateEvent.Set();
        _listener.Join(TimeSpan.FromSeconds(2));
        _activateEvent.Dispose();
        _stopEvent.Dispose();
        // Mutex ownership belongs to this same Main thread.
        try { _mutex.ReleaseMutex(); }
        finally { _mutex.Dispose(); }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
}
