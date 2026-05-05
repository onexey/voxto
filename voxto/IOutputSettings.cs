using System.Windows;

namespace Voxto;

/// <summary>
/// Contract for a self-contained output settings page shown as a dedicated tab in Preferences.
/// </summary>
public interface IOutputSettings
{
    string Id { get; }
    string DisplayName { get; }
    string TabTitle { get; }
    string Description { get; }
    FrameworkElement View { get; }
    void Load(AppSettings settings, OutputSettingsAdapter adapter);
    void Save(AppSettings settings, OutputSettingsAdapter adapter);
}
