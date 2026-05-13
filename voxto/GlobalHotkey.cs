using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
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
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int HOTKEY_ID = 9000;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly HotkeyMode _mode;
    private readonly int _virtualKey;
    private readonly ModifierKeys _modifiers;

    // Toggle mode
    private HwndSource? _hwndSource;
    private Window? _helperWindow;

    // Push-to-talk mode
    private IntPtr _hookHandle = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc; // keep delegate alive
    private readonly HashSet<int> _pressedKeys = [];
    private bool _shortcutIsDown;

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
    /// <param name="modifiers">Required modifier keys for the hotkey.</param>
    /// <param name="mode">Whether to use RegisterHotKey (Toggle) or a low-level hook (Push-to-talk).</param>
    public GlobalHotkey(int virtualKey, ModifierKeys modifiers, HotkeyMode mode)
    {
        _virtualKey = virtualKey;
        _modifiers = NormalizeModifiers(modifiers);
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

        RegisterHotKey(helper.Handle, HOTKEY_ID, GetRegistrationModifiers(_mode, _modifiers), (uint)_virtualKey);
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
            var message = wParam.ToInt32();

            if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                _pressedKeys.Add(vk);
            }
            else if (message is WM_KEYUP or WM_SYSKEYUP)
            {
                _pressedKeys.Remove(vk);
            }

            var shortcutIsDown = IsShortcutActive(_pressedKeys, _virtualKey, _modifiers);
            if (shortcutIsDown && !_shortcutIsDown)
            {
                _shortcutIsDown = true;
                Pressed?.Invoke();
            }
            else if (!shortcutIsDown && _shortcutIsDown)
            {
                _shortcutIsDown = false;
                Released?.Invoke();
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

    internal static uint GetRegistrationModifiers(HotkeyMode mode, ModifierKeys modifiers) =>
        ToNativeModifiers(modifiers) | (mode == HotkeyMode.Toggle ? MOD_NOREPEAT : 0);

    internal static bool TryBuildShortcut(Key key, ModifierKeys modifiers, out int virtualKey, out ModifierKeys normalizedModifiers)
    {
        normalizedModifiers = NormalizeModifiers(modifiers);
        virtualKey = 0;

        if (key == Key.None || IsModifierKey(key))
            return false;

        virtualKey = KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0;
    }

    internal static string FormatShortcut(ModifierKeys modifiers, int virtualKey)
    {
        var parts = new List<string>(5);
        modifiers = NormalizeModifiers(modifiers);

        if (modifiers.HasFlag(ModifierKeys.Control))
            parts.Add("Ctrl");

        if (modifiers.HasFlag(ModifierKeys.Shift))
            parts.Add("Shift");

        if (modifiers.HasFlag(ModifierKeys.Alt))
            parts.Add("Alt");

        if (modifiers.HasFlag(ModifierKeys.Windows))
            parts.Add("Win");

        if (virtualKey != 0)
            parts.Add(GetKeyDisplayName(virtualKey));

        return string.Join("+", parts);
    }

    internal static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;

    private static bool IsShortcutActive(ISet<int> pressedKeys, int virtualKey, ModifierKeys modifiers) =>
        pressedKeys.Contains(virtualKey) && GetActiveModifiers(pressedKeys) == NormalizeModifiers(modifiers);

    private static ModifierKeys GetActiveModifiers(ISet<int> pressedKeys)
    {
        var modifiers = ModifierKeys.None;

        if (pressedKeys.Contains(0x11) || pressedKeys.Contains(0xA2) || pressedKeys.Contains(0xA3))
            modifiers |= ModifierKeys.Control;

        if (pressedKeys.Contains(0x10) || pressedKeys.Contains(0xA0) || pressedKeys.Contains(0xA1))
            modifiers |= ModifierKeys.Shift;

        if (pressedKeys.Contains(0x12) || pressedKeys.Contains(0xA4) || pressedKeys.Contains(0xA5))
            modifiers |= ModifierKeys.Alt;

        if (pressedKeys.Contains(0x5B) || pressedKeys.Contains(0x5C))
            modifiers |= ModifierKeys.Windows;

        return modifiers;
    }

    private static ModifierKeys NormalizeModifiers(ModifierKeys modifiers) =>
        modifiers & (ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows);

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        var nativeModifiers = 0u;
        modifiers = NormalizeModifiers(modifiers);

        if (modifiers.HasFlag(ModifierKeys.Alt))
            nativeModifiers |= MOD_ALT;

        if (modifiers.HasFlag(ModifierKeys.Control))
            nativeModifiers |= MOD_CONTROL;

        if (modifiers.HasFlag(ModifierKeys.Shift))
            nativeModifiers |= MOD_SHIFT;

        if (modifiers.HasFlag(ModifierKeys.Windows))
            nativeModifiers |= MOD_WIN;

        return nativeModifiers;
    }

    private static string GetKeyDisplayName(int virtualKey)
    {
        var key = KeyInterop.KeyFromVirtualKey(virtualKey);
        if (key == Key.None)
            return $"VK 0x{virtualKey:X2}";

        return key switch
        {
            Key.D0 => "0",
            Key.D1 => "1",
            Key.D2 => "2",
            Key.D3 => "3",
            Key.D4 => "4",
            Key.D5 => "5",
            Key.D6 => "6",
            Key.D7 => "7",
            Key.D8 => "8",
            Key.D9 => "9",
            _ => GetConvertedKeyDisplayName(key, virtualKey)
        };
    }

    private static string GetConvertedKeyDisplayName(Key key, int virtualKey)
    {
        var displayName = TypeDescriptor.GetConverter(typeof(Key)).ConvertToInvariantString(key);
        return string.IsNullOrWhiteSpace(displayName) || string.Equals(displayName, nameof(Key.None), StringComparison.Ordinal)
            ? $"VK 0x{virtualKey:X2}"
            : displayName;
    }
}
