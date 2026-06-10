using System.IO;
using System.Text.Json;
using TransparentCalendar.Models;

namespace TransparentCalendar.Services;

public sealed class StorageService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string AppDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "透明日历");

    public string SettingsPath => Path.Combine(AppDataDirectory, "settings.json");
    public string CalendarDataPath => Path.Combine(AppDataDirectory, "calendar-data.json");

    public void EnsureDirectory()
    {
        Directory.CreateDirectory(AppDataDirectory);
    }

    public AppSettings LoadSettings()
    {
        EnsureDirectory();
        return LoadJson(SettingsPath, new AppSettings());
    }

    public void SaveSettings(AppSettings settings)
    {
        EnsureDirectory();
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, _jsonOptions));
    }

    public Dictionary<string, CalendarEntry> LoadEntries()
    {
        EnsureDirectory();
        return LoadJson(CalendarDataPath, new Dictionary<string, CalendarEntry>());
    }

    public void SaveEntries(Dictionary<string, CalendarEntry> entries)
    {
        EnsureDirectory();
        File.WriteAllText(CalendarDataPath, JsonSerializer.Serialize(entries, _jsonOptions));
    }

    private T LoadJson<T>(string path, T fallback)
    {
        if (!File.Exists(path))
        {
            return fallback;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions) ?? fallback;
        }
        catch
        {
            BackupBrokenFile(path);
            return fallback;
        }
    }

    private static void BackupBrokenFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var backupPath = $"{path}.broken-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        File.Move(path, backupPath, overwrite: true);
    }
}
