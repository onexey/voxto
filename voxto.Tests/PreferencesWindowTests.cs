using System.Runtime.ExceptionServices;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Voxto;
using Button = System.Windows.Controls.Button;
using Border = System.Windows.Controls.Border;
using TextBox = System.Windows.Controls.TextBox;
using WpfImage = System.Windows.Controls.Image;
using Xunit;
using TabControl = System.Windows.Controls.TabControl;

namespace Voxto.Tests;

public class PreferencesWindowTests
{
    [Fact]
    public void FormatVersionText_IncludesRevisionWhenPresent()
    {
        var text = PreferencesWindow.FormatVersionText(new Version(2026, 4, 27, 1));

        Assert.Equal("Version 2026.4.27.1", text);
    }

    [Fact]
    public void FormatVersionText_FallsBackWhenVersionMissing()
    {
        var text = PreferencesWindow.FormatVersionText(null);

        Assert.Equal("Version 1.0", text);
    }

    [Fact]
    public void Constructor_AddsDedicatedOutputTabs()
    {
        var headers = RunInSta(() =>
        {
            using var updateService = new UpdateService(new AppSettings());
            var window = new PreferencesWindow(new AppSettings(), new OutputManager(), updateService);
            var tabs = (TabControl)window.FindName("PreferencesTabs");

            return tabs.Items
                .OfType<TabItem>()
                .Select(item => item.Header?.ToString() ?? string.Empty)
                .ToArray();
        });

        Assert.Equal(
            ["General", "Markdown", "Todo", "Cursor", "About"],
            headers);
    }

    [Fact]
    public void Constructor_LoadsOutputTabsBeforeAboutTab()
    {
        var layout = RunInSta(() =>
        {
            using var updateService = new UpdateService(new AppSettings());
            var window = new PreferencesWindow(new AppSettings(), new OutputManager(), updateService);
            var tabs = (TabControl)window.FindName("PreferencesTabs");
            var aboutIndex = tabs.Items
                .OfType<TabItem>()
                .ToList()
                .FindIndex(item => Equals(item.Header, "About"));

            return (
                Count: tabs.Items.Count,
                AboutIndex: aboutIndex);
        });

        Assert.Equal(5, layout.Count);
        Assert.Equal(4, layout.AboutIndex);
    }

    [Fact]
    public void BuildSettings_PreservesEnabledOutputsWithoutSettingsPage()
    {
        var enabledOutputs = RunInSta(() =>
        {
            var current = new AppSettings
            {
                EnabledOutputs = ["MarkdownFile", "ExperimentalOutput"]
            };

            using var updateService = new UpdateService(current);
            var window = new PreferencesWindow(current, new OutputManager(), updateService);
            var method = typeof(PreferencesWindow).GetMethod("BuildSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var result = Assert.IsType<AppSettings>(method!.Invoke(window, null));
            return result.EnabledOutputs.ToArray();
        });

        Assert.Contains("ExperimentalOutput", enabledOutputs);
    }

    [Fact]
    public void Constructor_ShowsConfiguredShortcutInTextBox()
    {
        var hotkeyText = RunInSta(() =>
        {
            var current = new AppSettings
            {
                HotkeyVirtualKey = KeyInterop.VirtualKeyFromKey(Key.R),
                HotkeyModifiers = ModifierKeys.Control | ModifierKeys.Shift
            };

            using var updateService = new UpdateService(current);
            var window = new PreferencesWindow(current, new OutputManager(), updateService);
            var hotkeyTextBox = Assert.IsType<TextBox>(window.FindName("HotkeyTextBox"));
            return hotkeyTextBox.Text;
        });

        Assert.Equal("Ctrl+Shift+R", hotkeyText);
    }

    [Fact]
    public void BuildSettings_PreservesConfiguredHotkeyShortcut()
    {
        var hotkey = RunInSta(() =>
        {
            var current = new AppSettings
            {
                HotkeyVirtualKey = KeyInterop.VirtualKeyFromKey(Key.R),
                HotkeyModifiers = ModifierKeys.Control | ModifierKeys.Shift
            };

            using var updateService = new UpdateService(current);
            var window = new PreferencesWindow(current, new OutputManager(), updateService);
            var method = typeof(PreferencesWindow).GetMethod("BuildSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var result = Assert.IsType<AppSettings>(method!.Invoke(window, null));
            return (result.HotkeyVirtualKey, result.HotkeyModifiers);
        });

        Assert.Equal(KeyInterop.VirtualKeyFromKey(Key.R), hotkey.HotkeyVirtualKey);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, hotkey.HotkeyModifiers);
    }

    [Fact]
    public void RecordShortcutButton_ClickTogglesCaptureState()
    {
        var state = RunInSta(() =>
        {
            using var updateService = new UpdateService(new AppSettings());
            var window = new PreferencesWindow(new AppSettings(), new OutputManager(), updateService);
            var button = Assert.IsType<Button>(window.FindName("RecordHotkeyButton"));
            var hint = Assert.IsType<TextBlock>(window.FindName("HotkeyRecordingHintText"));

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var activeState = window.IsRecordingHotkey;
            var activeHint = hint.Visibility;

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return (
                ActiveState: activeState,
                ActiveHint: activeHint,
                InactiveState: window.IsRecordingHotkey,
                InactiveHint: hint.Visibility);
        });

        Assert.True(state.ActiveState);
        Assert.Equal(Visibility.Visible, state.ActiveHint);
        Assert.False(state.InactiveState);
        Assert.Equal(Visibility.Collapsed, state.InactiveHint);
    }

    [Fact]
    public void HandleHotkeyRecordingKey_EscapeCancelsCaptureWithoutChangingShortcut()
    {
        var state = RunInSta(() =>
        {
            var current = new AppSettings
            {
                HotkeyVirtualKey = KeyInterop.VirtualKeyFromKey(Key.R),
                HotkeyModifiers = ModifierKeys.Control
            };

            using var updateService = new UpdateService(current);
            var window = new PreferencesWindow(current, new OutputManager(), updateService);
            var button = Assert.IsType<Button>(window.FindName("RecordHotkeyButton"));
            var hotkeyTextBox = Assert.IsType<TextBox>(window.FindName("HotkeyTextBox"));

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var handled = window.HandleHotkeyRecordingKey(Key.Escape, ModifierKeys.None);

            return (
                Handled: handled,
                IsRecordingHotkey: window.IsRecordingHotkey,
                Text: hotkeyTextBox.Text);
        });

        Assert.True(state.Handled);
        Assert.False(state.IsRecordingHotkey);
        Assert.Equal("Ctrl+R", state.Text);
    }

    [Fact]
    public void HandleHotkeyRecordingKey_TabRemainsAvailableForNavigation()
    {
        var state = RunInSta(() =>
        {
            using var updateService = new UpdateService(new AppSettings());
            var window = new PreferencesWindow(new AppSettings(), new OutputManager(), updateService);
            var button = Assert.IsType<Button>(window.FindName("RecordHotkeyButton"));

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var handled = window.HandleHotkeyRecordingKey(Key.Tab, ModifierKeys.None);

            return (
                Handled: handled,
                IsRecordingHotkey: window.IsRecordingHotkey);
        });

        Assert.False(state.Handled);
        Assert.True(state.IsRecordingHotkey);
    }

    [Fact]
    public void Constructor_TabTemplateContainsFocusIndicatorBorder()
    {
        var hasFocusBorder = RunInSta(() =>
        {
            using var updateService = new UpdateService(new AppSettings());
            var window = new PreferencesWindow(new AppSettings(), new OutputManager(), updateService)
            {
                Left = -10000,
                Top = 0,
                ShowInTaskbar = false,
                ShowActivated = false
            };

            window.Show();
            window.UpdateLayout();

            try
            {
                var tabs = (TabControl)window.FindName("PreferencesTabs");
                var firstTab = tabs.Items.OfType<TabItem>().First();
                firstTab.ApplyTemplate();
                return firstTab.Template.FindName("TabFocusBorder", firstTab) is Border;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(hasFocusBorder);
    }

    [Fact]
    public void Constructor_TabHeadersSizeToTheirText()
    {
        var widthsAreSufficient = RunInSta(() =>
        {
            using var updateService = new UpdateService(new AppSettings());
            var window = new PreferencesWindow(new AppSettings(), new OutputManager(), updateService)
            {
                Left = -10000,
                Top = 0,
                ShowInTaskbar = false,
                ShowActivated = false
            };

            window.Show();
            window.UpdateLayout();

            try
            {
                var tabs = (TabControl)window.FindName("PreferencesTabs");
                return tabs.Items
                    .OfType<TabItem>()
                    .All(tab => tab.ActualWidth >= tab.DesiredSize.Width - 1);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(widthsAreSufficient);
    }

    [Fact]
    public void AboutTab_UsesScrollViewerForFullContentVisibility()
    {
        var usesScrollViewer = RunInSta(() =>
        {
            using var updateService = new UpdateService(new AppSettings());
            var window = new PreferencesWindow(new AppSettings(), new OutputManager(), updateService);
            var tabs = (TabControl)window.FindName("PreferencesTabs");
            var aboutTab = tabs.Items.OfType<TabItem>().Single(item => Equals(item.Header, "About"));

            return aboutTab.Content is ScrollViewer;
        });

        Assert.True(usesScrollViewer);
    }

    [Fact]
    public void AboutTab_UsesReadyPngResource()
    {
        var logoSource = RunInSta(() =>
        {
            using var updateService = new UpdateService(new AppSettings());
            var window = new PreferencesWindow(new AppSettings(), new OutputManager(), updateService);
            var logo = Assert.IsType<WpfImage>(window.FindName("AboutLogo"));
            var source = Assert.IsAssignableFrom<BitmapSource>(logo.Source);
            return source.ToString();
        });

        Assert.Contains("voxto-ready-128.png", logoSource);
    }

    private static T RunInSta<T>(Func<T> action)
    {
        T? result = default;
        Exception? capturedException = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                capturedException = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var completed = thread.Join(TimeSpan.FromSeconds(30));
        Assert.True(completed, "The STA test thread did not complete within 30 seconds.");
        if (capturedException is not null)
            ExceptionDispatchInfo.Capture(capturedException).Throw();

        return result!;
    }
}
