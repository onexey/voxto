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

        Opacity = 0;
        MessageText.Text = message;

        if (dotColor.HasValue)
            StatusDot.Fill = new SolidColorBrush(dotColor.Value);
    }

    // Position after layout is complete so SizeToContent has resolved the actual width.
    private void OnContentRendered(object sender, EventArgs e)
    {
        PositionBottomRight();
        Opacity = 1;
    }

    private void PositionBottomRight()
    {
        var screen = SystemParameters.WorkArea;
        var position = CalculateBottomRightPosition(screen, ActualWidth, ActualHeight);
        Left = position.X;
        Top = position.Y;
    }

    internal static System.Windows.Point CalculateBottomRightPosition(Rect workArea, double width, double height, double margin = 16)
    {
        return new System.Windows.Point(
            workArea.Right - width - margin,
            workArea.Bottom - height - margin);
    }
}
