using System.IO;
using System.Text.Json;
using TransparentCalendar.Models;


namespace TransparentCalendar.Services;

public sealed class StorageService
{
    private const int BackupKeepCount = 20;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // 监听线程（网页笔记）与 UI 线程会同时读写同一批文件，所有 Load/Save 都必须串行化。
    private readonly object _sync = new();
    private readonly List<string> _recoveredFiles = [];

    /// <summary>正式运行时的数据目录。</summary>
    public static string DefaultAppDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "透明日历");

    public string AppDataDirectory { get; }

    /// <param name="appDataDirectory">
    /// 仅供测试注入临时目录用；正式运行传 null 走 <see cref="DefaultAppDataDirectory"/>。
    /// </param>
    public StorageService(string? appDataDirectory = null)
    {
        AppDataDirectory = appDataDirectory ?? DefaultAppDataDirectory;
    }

    public string SettingsPath => Path.Combine(AppDataDirectory, "settings.json");
    public string CalendarDataPath => Path.Combine(AppDataDirectory, "calendar-data.json");
    public string WebNotesPath => Path.Combine(AppDataDirectory, "web-notes.json");
    public string BackupDirectory => Path.Combine(AppDataDirectory, "backups");
    public string LogDirectory => Path.Combine(AppDataDirectory, "logs");

    /// <summary>本次启动中因解析失败而被隔离的文件，供界面提示用户。</summary>
    public IReadOnlyList<string> RecoveredFiles
    {
        get
        {
            lock (_sync)
            {
                return _recoveredFiles.ToArray();
            }
        }
    }

    public void EnsureDirectory()
    {
        Directory.CreateDirectory(AppDataDirectory);
    }

    public AppSettings LoadSettings()
    {
        lock (_sync)
        {
            EnsureDirectory();
            return LoadJson(SettingsPath, new AppSettings()).Normalize();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        lock (_sync)
        {
            EnsureDirectory();
            WriteAtomic(SettingsPath, JsonSerializer.Serialize(settings, _jsonOptions));
        }
    }

    public List<WebNoteGroup> LoadWebNotes()
    {
        lock (_sync)
        {
            EnsureDirectory();
            return LoadJson(WebNotesPath, new List<WebNoteGroup>());
        }
    }

    public void SaveWebNotes(List<WebNoteGroup> notes)
    {
        lock (_sync)
        {
            EnsureDirectory();
            WriteAtomic(WebNotesPath, JsonSerializer.Serialize(notes, _jsonOptions));
        }
    }

    /// <summary>在锁内完成"读取-修改-写入"，避免与 UI 线程的保存互相覆盖。</summary>
    public List<WebNoteGroup> UpdateWebNotes(Action<List<WebNoteGroup>> update)
    {
        lock (_sync)
        {
            EnsureDirectory();
            var notes = LoadJson(WebNotesPath, new List<WebNoteGroup>());
            update(notes);
            WriteAtomic(WebNotesPath, JsonSerializer.Serialize(notes, _jsonOptions));
            return notes;
        }
    }

    public Dictionary<string, CalendarEntry> LoadEntries()
    {
        lock (_sync)
        {
            EnsureDirectory();
            return LoadJson(CalendarDataPath, new Dictionary<string, CalendarEntry>());
        }
    }

    public void SaveEntries(Dictionary<string, CalendarEntry> entries)
    {
        lock (_sync)
        {
            EnsureDirectory();
            WriteAtomic(CalendarDataPath, JsonSerializer.Serialize(entries, _jsonOptions));
        }
    }

    /// <summary>
    /// 写一份自动备份。<paramref name="force"/> 为 false 时，当天已有备份就跳过 ——
    /// 否则频繁开关应用会在几次启动内冲掉全部历史备份。
    /// </summary>
    public string? CreateAutomaticBackup(
        AppSettings settings,
        Dictionary<string, CalendarEntry> entries,
        bool force = true)
    {
        lock (_sync)
        {
            EnsureDirectory();
            Directory.CreateDirectory(BackupDirectory);

            if (!force && HasBackupForToday())
            {
                return null;
            }

            var backupPath = NextBackupPath();
            WriteBackup(backupPath, settings, entries);
            PruneBackups(BackupKeepCount);
            return backupPath;
        }
    }

    public void ExportBackup(string path, AppSettings settings, Dictionary<string, CalendarEntry> entries)
    {
        lock (_sync)
        {
            WriteBackup(path, settings, entries);
        }
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
        backup.Settings.Normalize();
        backup.Entries ??= [];
        return backup;
    }

    public void RestoreBackup(CalendarBackup backup)
    {
        SaveSettings(backup.Settings);
        SaveEntries(backup.Entries);
    }

    /// <summary>
    /// 文件名精度只到秒，同一秒内的两次强制备份会互相覆盖
    /// （导入前的保命备份就可能盖掉刚写的启动备份），因此冲突时追加序号。
    /// </summary>
    private string NextBackupPath()
    {
        var stamp = $"{DateTime.Now:yyyyMMdd-HHmmss}";
        var path = Path.Combine(BackupDirectory, $"backup-{stamp}.json");
        var suffix = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(BackupDirectory, $"backup-{stamp}-{suffix:00}.json");
            suffix++;
        }

        return path;
    }

    private bool HasBackupForToday()
    {
        if (!Directory.Exists(BackupDirectory))
        {
            return false;
        }

        return Directory
            .EnumerateFiles(BackupDirectory, $"backup-{DateTime.Now:yyyyMMdd}-*.json")
            .Any();
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
        catch (Exception ex)
        {
            Log.Error($"解析失败，隔离数据文件：{path}", ex);
            BackupBrokenFile(path);
            return fallback;
        }
    }

    private void BackupBrokenFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var backupPath = $"{path}.broken-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        File.Move(path, backupPath, overwrite: true);
        _recoveredFiles.Add(backupPath);
    }

    /// <summary>
    /// 先写临时文件再替换，避免写入过程中崩溃/断电把原文件截断成半截 JSON
    /// （那会在下次启动时被当成损坏文件隔离掉，等同于丢数据）。
    /// </summary>
    private static void WriteAtomic(string path, string content)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content);

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, path);
        }
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

        WriteAtomic(path, JsonSerializer.Serialize(backup, _jsonOptions));
    }

    private void PruneBackups(int keepCount)
    {
        if (!Directory.Exists(BackupDirectory))
        {
            return;
        }

        // 文件名内嵌 yyyyMMdd-HHmmss，字典序即时间序；创建时间在 Windows 上会因
        // 文件隧道（file tunneling）继承旧值，不可靠。
        var backupFiles = Directory.GetFiles(BackupDirectory, "backup-*.json")
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
            .Skip(keepCount);

        foreach (var backupFile in backupFiles)
        {
            try
            {
                File.Delete(backupFile);
            }
            catch
            {
                // 单个旧备份删不掉不影响主流程。
            }
        }
    }
}
