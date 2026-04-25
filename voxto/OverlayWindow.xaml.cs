using System.Windows;

namespace Voxto;

/// <summary>
/// A small always-on-top overlay window shown in the bottom-right corner while recording is active.
/// The window is non-interactive (IsHitTestVisible = false in XAML) so it never steals focus.
/// </summary>
public partial class OverlayWindow : Window
{
    /// <summary>Initialises the overlay and positions it in the bottom-right of the work area.</summary>
    public OverlayWindow()
    {
        InitializeComponent();
        PositionBottomRight();
    }

    private void PositionBottomRight()
    {
        var screen = SystemParameters.WorkArea;
        Left = screen.Right - Width - 16;
        Top  = screen.Bottom - Height - 16;
    }
}
