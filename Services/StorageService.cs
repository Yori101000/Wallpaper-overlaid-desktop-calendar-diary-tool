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
    public string BackupDirectory => Path.Combine(AppDataDirectory, "backups");

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

    public string CreateAutomaticBackup(AppSettings settings, Dictionary<string, CalendarEntry> entries)
    {
        EnsureDirectory();
        Directory.CreateDirectory(BackupDirectory);

        var backupPath = Path.Combine(BackupDirectory, $"backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        WriteBackup(backupPath, settings, entries);
        PruneBackups(10);
        return backupPath;
    }

    public void ExportBackup(string path, AppSettings settings, Dictionary<string, CalendarEntry> entries)
    {
        WriteBackup(path, settings, entries);
    }

    public CalendarBackup LoadBackup(string path)
    {
        var json = File.ReadAllText(path);
        var backup = JsonSerializer.Deserialize<CalendarBackup>(json, _jsonOptions);
        if (backup is null)
        {
            throw new InvalidDataException("备份文件内容为空。");
        }

        backup.Settings ??= new AppSettings();
        backup.Entries ??= [];
        return backup;
    }

    public void RestoreBackup(CalendarBackup backup)
    {
        SaveSettings(backup.Settings);
        SaveEntries(backup.Entries);
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

    private void WriteBackup(string path, AppSettings settings, Dictionary<string, CalendarEntry> entries)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var backup = new CalendarBackup
        {
            ExportedAt = DateTime.Now,
            Settings = settings,
            Entries = entries
        };

        File.WriteAllText(path, JsonSerializer.Serialize(backup, _jsonOptions));
    }

    private void PruneBackups(int keepCount)
    {
        if (!Directory.Exists(BackupDirectory))
        {
            return;
        }

        var backupFiles = Directory.GetFiles(BackupDirectory, "backup-*.json")
            .OrderByDescending(File.GetCreationTime)
            .Skip(keepCount);

        foreach (var backupFile in backupFiles)
        {
            File.Delete(backupFile);
        }
    }
}
