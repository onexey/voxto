using System.IO;
using System.Windows;
using Serilog;
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
        InitialiseLogging();

        // ── Global exception handlers ────────────────────────────────────────
        // Catch unhandled exceptions on the WPF dispatcher thread.
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error(ex.Exception, "Unhandled dispatcher exception");
            ex.Handled = true; // keep the tray app alive
        };

        // Catch unhandled exceptions on background/thread-pool threads.
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            Log.Fatal(ex.ExceptionObject as Exception, "Unhandled domain exception (terminating={IsTerminating})", ex.IsTerminating);
            Log.CloseAndFlush();
        };

        base.OnStartup(e);

        Log.Information("Voxto {Version} started",
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);

        _trayIcon = new TrayIcon();
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Voxto exiting (code {Code})", e.ApplicationExitCode);
        _trayIcon?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    // ── Logging setup ─────────────────────────────────────────────────────────

    private static void InitialiseLogging()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Voxto", "logs");

        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                path: Path.Combine(logDir, "voxto-.log"),
                rollingInterval:        RollingInterval.Day,
                retainedFileCountLimit: 30,           // auto-deletes files older than 30 days
                buffered:               false,        // flush immediately so crashes are captured
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
