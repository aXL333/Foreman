using System.Text.Json;

namespace Foreman.Core.Settings;

public sealed class SettingsStore
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Foreman", "settings.json");

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    public static ForemanSettings Load()
    {
        if (!File.Exists(_path)) return new ForemanSettings();
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<ForemanSettings>(json, _opts) ?? new ForemanSettings();
        }
        catch
        {
            return new ForemanSettings();
        }
    }

    public static void Save(ForemanSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, _opts));
    }
}
