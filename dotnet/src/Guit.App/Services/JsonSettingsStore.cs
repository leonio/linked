using System.Text.Json;
using System.Text.Json.Serialization;
using Guit.Core.Services;

namespace Guit.App.Services;

/// <summary>
/// Settings as a small JSON file under %LOCALAPPDATA%\Guit. (ApplicationData.Current needs a packaged app;
/// this one is unpackaged.) Reads once, writes on every change; a damaged file is treated as empty.
/// </summary>
internal sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guit", "settings.json");

    private Dictionary<string, string>? _values;

    public string? Read(string key) => Load().GetValueOrDefault(key);

    public void Write(string key, string value)
    {
        Dictionary<string, string> values = Load();
        values[key] = value;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(values, SettingsJsonContext.Default.DictionaryStringString));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The setting still applies for this run; it just will not be remembered.
        }
    }

    private Dictionary<string, string> Load()
    {
        if (_values is not null)
        {
            return _values;
        }

        try
        {
            if (File.Exists(_path))
            {
                _values = JsonSerializer.Deserialize(File.ReadAllText(_path), SettingsJsonContext.Default.DictionaryStringString);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return _values ??= [];
    }
}

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
