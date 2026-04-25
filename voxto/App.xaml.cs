using System.Windows;
using Application = System.Windows.Application;

namespace Voxto;

/// <summary>
/// WPF application entry point.
/// Sets <c>ShutdownMode="OnExplicitShutdown"</c> in XAML so the process stays alive
/// without a main window; shutdown is triggered from <see cref="TrayIcon"/> when the user
/// clicks Exit.
/// </summary>
public partial class App : Application
{
    private TrayIcon? _trayIcon;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _trayIcon = new TrayIcon();
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
