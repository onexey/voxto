using System.Windows;

namespace Voxto;

/// <summary>
/// Contract for a self-contained output settings page shown as a dedicated tab in Preferences.
/// </summary>
internal interface IOutputSettings
{
    string OutputId { get; }
    string TabTitle { get; }
    FrameworkElement View { get; }
    void Load(AppSettings settings, OutputSettingsAdapter adapter);
    void Save(AppSettings settings, OutputSettingsAdapter adapter);
}
