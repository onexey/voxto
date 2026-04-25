using Microsoft.Win32;

namespace Voxto;

/// <summary>
/// Adds or removes Voxto from the current user's Windows startup entries by
/// writing to <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>.
/// </summary>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName    = "Voxto";

    /// <summary>Returns <see langword="true"/> if a startup entry for Voxto currently exists.</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(AppName) is not null;
    }

    /// <summary>
    /// Writes a startup entry pointing to the running executable.
    /// Does nothing if <see cref="Environment.ProcessPath"/> cannot be determined.
    /// </summary>
    public static void Enable()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return;

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        // Quote the path so spaces in folder names are handled correctly.
        key?.SetValue(AppName, $"\"{exe}\"");
    }

    /// <summary>Removes the startup entry. Safe to call even if no entry exists.</summary>
    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
    }
}
