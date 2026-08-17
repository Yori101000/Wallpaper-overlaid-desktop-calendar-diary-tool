// WPF/WinForms 工程的隐式 using 不含 System.IO（Path 会与 System.Windows.Shapes.Path 冲突）
using System.IO;
using System.Text.Json;
using TransparentCalendar.Models;
using TransparentCalendar.Services;
using Xunit;

namespace TransparentCalendar.Tests;

/// <summary>
/// 存储层是唯一会造成"用户数据没了"的地方，因此这里覆盖得最密。
/// 所有用例都在**临时目录**里跑，绝不触碰真实的 %AppData%\透明日历。
/// </summary>
public sealed class StorageServiceTests : IDisposable
{
    private readonly string _root;
    private readonly StorageService _storage;

    public StorageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tc-tests-" + Guid.NewGuid().ToString("N"));
        _storage = new StorageService(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // 临时目录清理失败不影响测试结论。
        }
    }

    private static Dictionary<string, CalendarEntry> SampleEntries() => new()
    {
        ["2026-08-14"] = new CalendarEntry
        {
            Date = "2026-08-14",
            Diary = "今天写了测试",
            Todos = [new TodoItem { Text = "写测试", Priority = "重要" }]
        }
    };

    [Fact]
    public void 保存后能原样读回()
    {
        _storage.SaveEntries(SampleEntries());

        var loaded = _storage.LoadEntries();

        Assert.Single(loaded);
        Assert.Equal("今天写了测试", loaded["2026-08-14"].Diary);
        Assert.Equal("写测试", loaded["2026-08-14"].Todos[0].Text);
    }

    [Fact]
    public void 写入是原子的_不留临时文件()
    {
        _storage.SaveEntries(SampleEntries());
        _storage.SaveEntries(SampleEntries());

        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void 覆盖写入不会丢失原文件()
    {
        _storage.SaveSettings(new AppSettings { FontSize = 20 });
        _storage.SaveSettings(new AppSettings { FontSize = 40 });

        Assert.Equal(40, _storage.LoadSettings().FontSize);
        Assert.True(File.Exists(_storage.SettingsPath));
    }

    [Fact]
    public void 损坏的文件会被隔离并记入_RecoveredFiles()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_storage.CalendarDataPath, "{ 这不是合法 JSON");

        var loaded = _storage.LoadEntries();

        Assert.Empty(loaded);
        Assert.Single(_storage.RecoveredFiles);
        Assert.Contains(".broken-", _storage.RecoveredFiles[0], StringComparison.Ordinal);
        Assert.True(File.Exists(_storage.RecoveredFiles[0]));
    }

    [Fact]
    public void 隔离文件保留了原始内容以便人工找回()
    {
        Directory.CreateDirectory(_root);
        const string broken = "{ 半截 JSON 内容";
        File.WriteAllText(_storage.WebNotesPath, broken);

        _storage.LoadWebNotes();

        Assert.Equal(broken, File.ReadAllText(_storage.RecoveredFiles[0]));
    }

    [Fact]
    public void 文件不存在时返回默认值且不算隔离()
    {
        Assert.Empty(_storage.LoadEntries());
        Assert.Empty(_storage.LoadWebNotes());
        Assert.Empty(_storage.RecoveredFiles);
    }

    [Fact]
    public void 启动备份按天去重()
    {
        var settings = new AppSettings();
        var entries = SampleEntries();

        var first = _storage.CreateAutomaticBackup(settings, entries, force: false);
        var second = _storage.CreateAutomaticBackup(settings, entries, force: false);
        var third = _storage.CreateAutomaticBackup(settings, entries, force: false);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Null(third);
        Assert.Single(Directory.GetFiles(_storage.BackupDirectory, "backup-*.json"));
    }

    [Fact]
    public void force_备份始终写入_用于覆盖数据前保命()
    {
        var settings = new AppSettings();
        var entries = SampleEntries();

        _storage.CreateAutomaticBackup(settings, entries, force: false);
        var forced = _storage.CreateAutomaticBackup(settings, entries, force: true);

        Assert.NotNull(forced);
        Assert.Equal(2, Directory.GetFiles(_storage.BackupDirectory, "backup-*.json").Length);
    }

    [Fact]
    public void 备份修剪按文件名时间戳保留最新的若干份()
    {
        Directory.CreateDirectory(_storage.BackupDirectory);

        // 构造 25 份跨越多天的历史备份，文件名内嵌时间戳（字典序即时间序）。
        for (var day = 1; day <= 25; day++)
        {
            File.WriteAllText(
                Path.Combine(_storage.BackupDirectory, $"backup-202601{day:00}-120000.json"),
                "{}");
        }

        _storage.CreateAutomaticBackup(new AppSettings(), SampleEntries(), force: true);

        var remaining = Directory.GetFiles(_storage.BackupDirectory, "backup-*.json")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(20, remaining.Count);
        // 被删掉的必须是文件名最小（最早）的那批，而不是任意几份
        Assert.DoesNotContain("backup-20260101-120000.json", remaining);
        Assert.DoesNotContain("backup-20260105-120000.json", remaining);
        Assert.Contains("backup-20260125-120000.json", remaining);
    }

    [Fact]
    public void 备份可导出并原样恢复()
    {
        var settings = new AppSettings { FontSize = 33, WindowLayer = WindowLayers.Bottom };
        var entries = SampleEntries();
        var path = Path.Combine(_root, "export.json");

        _storage.ExportBackup(path, settings, entries);
        var restored = _storage.LoadBackup(path);

        Assert.Equal(33, restored.Settings.FontSize);
        Assert.Equal(WindowLayers.Bottom, restored.Settings.WindowLayer);
        Assert.Equal("今天写了测试", restored.Entries["2026-08-14"].Diary);
    }

    [Fact]
    public void 载入备份时同样执行设置迁移()
    {
        var path = Path.Combine(_root, "legacy.json");
        Directory.CreateDirectory(_root);
        // 模拟旧版本导出的备份：只有 KeepOnTop，没有 WindowLayer
        File.WriteAllText(path, """
        {
          "Version": 1,
          "Settings": { "KeepOnTop": true, "FontSize": 28 },
          "Entries": {}
        }
        """);

        var backup = _storage.LoadBackup(path);

        Assert.Equal(WindowLayers.Top, backup.Settings.WindowLayer);
        Assert.Null(backup.Settings.KeepOnTop);
    }

    [Fact]
    public void UpdateWebNotes_在锁内完成读改写()
    {
        _storage.SaveWebNotes([new WebNoteGroup { Id = "a", Url = "https://example.com", Title = "示例" }]);

        var result = _storage.UpdateWebNotes(notes =>
        {
            notes.Add(new WebNoteGroup { Id = "b", Url = "https://example.org", Title = "另一个" });
        });

        Assert.Equal(2, result.Count);
        Assert.Equal(2, _storage.LoadWebNotes().Count);
    }

    [Fact]
    public void UpdateWebNotes_并发写入不会互相覆盖()
    {
        _storage.SaveWebNotes([]);

        Parallel.For(0, 50, i =>
        {
            _storage.UpdateWebNotes(notes =>
                notes.Add(new WebNoteGroup { Id = i.ToString(), Url = $"https://example.com/{i}" }));
        });

        Assert.Equal(50, _storage.LoadWebNotes().Count);
    }

    [Fact]
    public void 设置载入时执行迁移()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(_storage.SettingsPath, JsonSerializer.Serialize(new
        {
            KeepOnTop = true,
            AttachToDesktopLayer = true,
            FontSize = 26.0
        }));

        var settings = _storage.LoadSettings();

        Assert.Equal(WindowLayers.Top, settings.WindowLayer);
        Assert.Equal(26.0, settings.FontSize);
    }

    [Fact]
    public void 保存后的设置文件不再包含已迁移的旧字段()
    {
        _storage.SaveSettings(new AppSettings { WindowLayer = WindowLayers.Bottom }.Normalize());

        var json = File.ReadAllText(_storage.SettingsPath);

        Assert.DoesNotContain("KeepOnTop", json, StringComparison.Ordinal);
        Assert.DoesNotContain("AttachToDesktopLayer", json, StringComparison.Ordinal);
        Assert.Contains("\"WindowLayer\": \"Bottom\"", json, StringComparison.Ordinal);
    }
}
