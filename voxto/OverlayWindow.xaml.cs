using System.Windows;
using System.Windows.Media.Imaging;

namespace Voxto;

/// <summary>
/// A small always-on-top pill shown in the bottom-right corner to communicate app state.
/// Non-interactive (IsHitTestVisible = false in XAML) so it never steals focus.
/// The left-side indicator is the voxto brand icon at 32 px for the current
/// <see cref="AppState"/>, loaded from the embedded icon-pack resources.
/// </summary>
public partial class OverlayWindow : Window
{
    /// <summary>
    /// Creates the overlay pill.
    /// </summary>
    /// <param name="message">Text displayed inside the pill.</param>
    /// <param name="state">
    /// App state used to select the correct brand icon (ready / recording / transcribing).
    /// Defaults to <see cref="AppState.Recording"/>.
    /// </param>
    public OverlayWindow(string message = "Recording…", AppState state = AppState.Recording)
    {
        InitializeComponent();

        Opacity          = 0;
        MessageText.Text = message;
        StateIcon.Source = LoadStateIcon(state);
    }

    // ── Icon loading ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a <see cref="BitmapImage"/> for the 32-px brand PNG matching
    /// <paramref name="state"/>, loaded from the WPF pack:// resource store.
    /// </summary>
    private static BitmapImage LoadStateIcon(AppState state)
    {
        string name = state switch
        {
            AppState.Recording    => "recording",
            AppState.Transcribing => "transcribing",
            _                     => "ready",
        };

        var uri = new Uri(
            $"pack://application:,,,/Assets/icons/png/{name}/voxto-{name}-32.png",
            UriKind.Absolute);

        return new BitmapImage(uri);
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    // Position after layout is complete so SizeToContent has resolved the actual width.
    private void OnContentRendered(object sender, EventArgs e)
    {
        PositionBottomRight();
        Opacity = 1;
    }

    private void PositionBottomRight()
    {
        var screen   = SystemParameters.WorkArea;
        var position = CalculateBottomRightPosition(screen, ActualWidth, ActualHeight);
        Left = position.X;
        Top  = position.Y;
    }

    internal static System.Windows.Point CalculateBottomRightPosition(
        Rect workArea, double width, double height, double margin = 16)
    {
        return new System.Windows.Point(
            workArea.Right  - width  - margin,
            workArea.Bottom - height - margin);
    }
}
