using System.Text.Json;

namespace Voxto;

/// <summary>
/// Reads and writes typed per-output configuration inside <see cref="AppSettings.OutputSettings"/>.
/// </summary>
public sealed class OutputSettingsAdapter(AppSettings settings)
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    public T Get<T>(string outputId) where T : new()
    {
        if (settings.OutputSettings.TryGetValue(outputId, out var stored))
        {
            try
            {
                var loaded = stored.Deserialize<T>(SerializerOptions);
                if (loaded is not null)
                    return loaded;
            }
            catch (JsonException)
            {
                // Fall through to defaults for malformed persisted output settings.
            }
            catch (NotSupportedException)
            {
                // Fall through to defaults for unsupported persisted output settings.
            }
        }

        return new T();
    }

    public void Set<T>(string outputId, T value)
    {
        settings.OutputSettings[outputId] = JsonSerializer.SerializeToElement(value, SerializerOptions);
    }
}
