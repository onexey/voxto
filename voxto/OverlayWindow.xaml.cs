using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace Voxto;

/// <summary>
/// A small always-on-top pill shown in the bottom-right corner to communicate app state.
/// Non-interactive (IsHitTestVisible = false in XAML) so it never steals focus.
/// </summary>
public partial class OverlayWindow : Window
{
    /// <summary>
    /// Creates the overlay pill.
    /// </summary>
    /// <param name="message">Text displayed inside the pill.</param>
    /// <param name="dotColor">
    /// Colour of the pulsing status dot.
    /// Defaults to red (<c>#EF4444</c>) — the recording colour.
    /// </param>
    public OverlayWindow(string message = "Recording…", Color? dotColor = null)
    {
        InitializeComponent();

        MessageText.Text = message;

        if (dotColor.HasValue)
            StatusDot.Fill = new SolidColorBrush(dotColor.Value);
    }

    // Position after layout is complete so SizeToContent has resolved the actual width.
    private void OnContentRendered(object sender, EventArgs e) => PositionBottomRight();

    private void PositionBottomRight()
    {
        var screen = SystemParameters.WorkArea;
        Left = screen.Right - ActualWidth - 16;
        Top  = screen.Bottom - ActualHeight - 16;
    }
}
