using System.Text.Json;
using Serilog;

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
            catch (JsonException ex)
            {
                Log.Warning(ex, "Failed to deserialize output settings for {OutputId}. Falling back to defaults.", outputId);
            }
            catch (NotSupportedException ex)
            {
                Log.Warning(ex, "Unsupported output settings payload for {OutputId}. Falling back to defaults.", outputId);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Unexpected error while loading output settings for {OutputId}. Falling back to defaults.", outputId);
            }
        }

        return new T();
    }

    public void Set<T>(string outputId, T value)
    {
        settings.OutputSettings[outputId] = JsonSerializer.SerializeToElement(value, SerializerOptions);
    }
}
