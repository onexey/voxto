using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Voxto;

/// <summary>
/// Registers a global hotkey (works even when the app is not focused).
/// Supports both Toggle mode (RegisterHotKey) and Push-to-Talk mode (low-level keyboard hook).
/// </summary>
public class GlobalHotkey : IDisposable
{
    // Win32
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int HOTKEY_ID = 9000;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly HotkeyMode _mode;
    private readonly int _virtualKey;

    // Toggle mode
    private HwndSource? _hwndSource;
    private Window? _helperWindow;

    // Push-to-talk mode
    private IntPtr _hookHandle = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc; // keep delegate alive
    private bool _keyIsDown;

    /// <summary>
    /// Fired when the hotkey is pressed.
    /// In Toggle mode this fires on every key-down; in Push-to-talk mode it fires when the key goes down.
    /// </summary>
    public event Action? Pressed;

    /// <summary>
    /// Fired when the hotkey is released. Only used in Push-to-talk mode.
    /// </summary>
    public event Action? Released;

    /// <summary>
    /// Registers a global hotkey for the given virtual-key code and mode.
    /// </summary>
    /// <param name="virtualKey">Win32 virtual-key code (e.g. <c>0x78</c> for F9).</param>
    /// <param name="mode">Whether to use RegisterHotKey (Toggle) or a low-level hook (Push-to-talk).</param>
    public GlobalHotkey(int virtualKey, HotkeyMode mode)
    {
        _virtualKey = virtualKey;
        _mode = mode;

        if (mode == HotkeyMode.Toggle)
            RegisterToggle();
        else
            RegisterPushToTalk();
    }

    // ── Toggle ───────────────────────────────────────────────────────────────

    private void RegisterToggle()
    {
        // We need a real HWND; create a hidden helper window on the UI thread
        _helperWindow = new Window
        {
            Width = 0, Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Visibility = Visibility.Hidden
        };
        _helperWindow.Show();

        var helper = new WindowInteropHelper(_helperWindow);
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource.AddHook(WndProc);

        RegisterHotKey(helper.Handle, HOTKEY_ID, GetRegistrationModifiers(_mode), (uint)_virtualKey);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == HOTKEY_ID) // WM_HOTKEY
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ── Push-to-talk ─────────────────────────────────────────────────────────

    private void RegisterPushToTalk()
    {
        _hookProc = HookCallback;
        var module = GetModuleHandle(null);
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, module, 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var vk = Marshal.ReadInt32(lParam);
            if (vk == _virtualKey)
            {
                if (wParam.ToInt32() == WM_KEYDOWN && !_keyIsDown)
                {
                    _keyIsDown = true;
                    Pressed?.Invoke();
                }
                else if (wParam.ToInt32() == WM_KEYUP && _keyIsDown)
                {
                    _keyIsDown = false;
                    Released?.Invoke();
                }
            }
        }
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_mode == HotkeyMode.Toggle)
        {
            if (_hwndSource != null)
            {
                UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID);
                _hwndSource.RemoveHook(WndProc);
            }
            _helperWindow?.Close();
        }
        else
        {
            if (_hookHandle != IntPtr.Zero)
                UnhookWindowsHookEx(_hookHandle);
        }
    }

    internal static uint GetRegistrationModifiers(HotkeyMode mode) =>
        mode == HotkeyMode.Toggle ? MOD_NOREPEAT : 0;
}
