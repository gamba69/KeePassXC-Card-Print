using System.Runtime.InteropServices;

namespace KeePassXCCardPrint.Platforms.Windows.Input;

/// <summary>
/// Switches the current UI thread to an already loaded English input locale
/// only while the master-password field is active. Never loads a layout,
/// changes other processes, observes keystrokes or persists text.
/// </summary>
internal sealed class WindowsPasswordInputLanguage : IPasswordInputLanguageScope
{
    private nint _previous;
    private nint _chosen;
    private bool _switched;

    internal static bool IsEnglish(nint layout) =>
        ((((ushort)((long)layout & 0xFFFF)) & 0x03FF) == 0x0009);

    public void Enter()
    {
        if (!OperatingSystem.IsWindows() || _switched) return;
        var original = GetKeyboardLayout(0);
        if (original == 0 || IsEnglish(original)) return;
        int count = GetKeyboardLayoutList(0, null);
        if (count <= 0 || count > 256) return;
        var layouts = new nint[count];
        int actual = GetKeyboardLayoutList(layouts.Length, layouts);
        if (actual <= 0) return;

        nint english = 0;
        // Use US English if installed; otherwise accept another English locale.
        for (int i = 0; i < Math.Min(actual, layouts.Length); i++)
        {
            var candidate = layouts[i];
            if (!IsEnglish(candidate)) continue;
            if (english == 0) english = candidate;
            if ((((long)candidate) & 0xFFFF) == 0x0409)
            {
                english = candidate;
                break;
            }
        }
        if (english == 0 || english == original) return;
        // Zero flags: change the CURRENT THREAD only, not the whole process.
        var previous = ActivateKeyboardLayout(english, 0);
        if (previous == 0 || GetKeyboardLayout(0) != english) return;
        _previous = previous;
        _chosen = english;
        _switched = true;
    }

    public void Leave()
    {
        if (!_switched) return;
        _switched = false;
        if (OperatingSystem.IsWindows() && _previous != 0 &&
            GetKeyboardLayout(0) == _chosen)
            ActivateKeyboardLayout(_previous, 0);
        // If a user deliberately switched the language while entering the
        // password, leave their new choice intact.
        _previous = 0;
        _chosen = 0;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetKeyboardLayoutList(int nBuff, [Out] nint[]? lpList);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint ActivateKeyboardLayout(nint hkl, uint flags);
}
