using System.IO;
using System.Text.Json;
using ArgbSync.App.Models;

namespace ArgbSync.App.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _directory;
    private readonly string _settingsPath;

    public SettingsService(string directory)
    {
        _directory = directory;
        _settingsPath = Path.Combine(directory, "settings.json");
    }

    public ArgbSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new ArgbSettings();

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<ArgbSettings>(json, JsonOptions) ?? new ArgbSettings();
        }
        catch
        {
            return new ArgbSettings();
        }
    }

    public void Save(ArgbSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
        }
    }
}