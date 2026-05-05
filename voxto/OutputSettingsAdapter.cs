using System.Text.Json;

namespace Voxto;

/// <summary>
/// Reads and writes typed per-output configuration inside <see cref="AppSettings.OutputSettings"/>.
/// </summary>
internal sealed class OutputSettingsAdapter(AppSettings settings)
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    public T Get<T>(string outputId, Func<T> defaultFactory, Func<AppSettings, T>? legacyFactory = null)
    {
        if (settings.OutputSettings.TryGetValue(outputId, out var stored))
        {
            try
            {
                var loaded = stored.Deserialize<T>(SerializerOptions);
                if (loaded is not null)
                    return loaded;
            }
            catch
            {
                // Fall through to legacy/default settings.
            }
        }

        return legacyFactory is not null
            ? legacyFactory(settings)
            : defaultFactory();
    }

    public void Set<T>(string outputId, T value)
    {
        settings.OutputSettings[outputId] = JsonSerializer.SerializeToElement(value, SerializerOptions);
    }
}
