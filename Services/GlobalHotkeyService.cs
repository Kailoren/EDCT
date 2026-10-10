using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ColonyTracker.Services;

/// <summary>Wraps Win32 RegisterHotKey/UnregisterHotKey plus a WM_HOTKEY message hook so the
/// overlay's show/hide and lock/unlock keybinds fire system-wide - even while Elite Dangerous has
/// focus, which a plain WPF KeyDown handler can't do since that only sees input while this app's
/// own window is focused. Registered against MainWindow specifically (not the OverlayWindow,
/// which is created/destroyed as it's shown/hidden - registering there would mean the "show
/// overlay" hotkey stops working the moment the overlay is hidden, exactly when it's needed).
/// Hooks via SourceInitialized rather than Loaded since that's the first point the window's HWND
/// actually exists, which every Win32 call here needs.</summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int ProbeId = 0x7000; // scratch id used only to test-register a candidate combo, never left registered

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Window _window;
    private readonly Dictionary<int, Action> _handlers = [];
    private HwndSource? _source;

    public GlobalHotkeyService(Window window)
    {
        _window = window;
        if (PresentationSource.FromVisual(window) is HwndSource alreadySource)
            Hook(alreadySource);
        else
            window.SourceInitialized += (_, _) => Hook((HwndSource)PresentationSource.FromVisual(window)!);
    }

    private void Hook(HwndSource source)
    {
        _source = source;
        source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>Registers (replacing any previous registration under this same id) a global hotkey
    /// that invokes <paramref name="action"/> when pressed anywhere on the system. Returns false if
    /// the OS wouldn't grant it - usually because another app (or the game) already claimed that
    /// exact combo.</summary>
    public bool Register(int id, ModifierKeys modifiers, Key key, Action action)
    {
        Unregister(id);
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero) return false; // window not yet shown; nothing to register against
        if (!RegisterHotKey(hwnd, id, (uint)modifiers | MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(key)))
            return false;
        _handlers[id] = action;
        return true;
    }

    public void Unregister(int id)
    {
        if (!_handlers.Remove(id)) return;
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd != IntPtr.Zero) UnregisterHotKey(hwnd, id);
    }

    /// <summary>Tests whether a candidate combo is currently available, registering and
    /// immediately unregistering it under a scratch id rather than leaving it claimed - used by
    /// Settings to validate a freshly-captured keybind before committing it, so a conflict
    /// surfaces immediately instead of silently failing on next launch.</summary>
    public bool Probe(ModifierKeys modifiers, Key key)
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero) return false;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!RegisterHotKey(hwnd, ProbeId, (uint)modifiers | MOD_NOREPEAT, vk)) return false;
        UnregisterHotKey(hwnd, ProbeId);
        return true;
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys.ToList())
            Unregister(id);
        if (_source is not null)
            _source.RemoveHook(WndProc);
    }
}
