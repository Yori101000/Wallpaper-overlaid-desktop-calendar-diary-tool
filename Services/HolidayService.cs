using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TransparentCalendar.Services;

/// <summary>某一天的法定假日信息。<see cref="IsOffDay"/> 为 false 表示"调休上班"。</summary>
public sealed record HolidayInfo(string Name, bool IsOffDay);

/// <summary>落盘缓存的自有格式 —— 与数据源解耦，换源不影响已有缓存。</summary>
public sealed class HolidayYearCache
{
    public int Year { get; set; }
    public DateTime FetchedAt { get; set; }
    public string Source { get; set; } = string.Empty;
    public Dictionary<string, HolidayDay> Days { get; set; } = [];
}

public sealed class HolidayDay
{
    public string Name { get; set; } = string.Empty;
    public bool IsOffDay { get; set; }
}

/// <summary>
/// 法定节假日与调休。
///
/// **这是本应用唯一的对外网络请求。** 之所以必须联网：调休安排由国务院每年单独发文公布，
/// 不存在可推算的规则，Windows 与 .NET 也都没有开放查询接口。
///
/// 行为约束：异步拉取、绝不阻塞 UI；失败时静默降级（用缓存 → 无缓存则不显示角标），
/// 日历其余功能完全不受影响；设置里关闭后一个请求都不会发。
/// </summary>
public sealed class HolidayService
{
    /// <summary>当年数据的缓存有效期 —— 国务院可能补发通知，过期后重拉。</summary>
    private static readonly TimeSpan CurrentYearCacheLifetime = TimeSpan.FromDays(30);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _cacheDirectory;
    private readonly Dictionary<int, Dictionary<DateTime, HolidayInfo>> _memory = [];
    private readonly HashSet<int> _inFlight = [];
    private readonly object _sync = new();

    /// <summary>某一年的数据就绪（无论来自缓存还是网络），参数是年份。</summary>
    public event Action<int>? YearLoaded;

    public HolidayService(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
    }

    /// <summary>同步查询，只看已载入内存的数据；没有就返回 null，绝不阻塞。</summary>
    public HolidayInfo? Find(DateTime date)
    {
        lock (_sync)
        {
            return _memory.TryGetValue(date.Year, out var year) && year.TryGetValue(date.Date, out var info)
                ? info
                : null;
        }
    }

    /// <summary>
    /// 确保某年的数据可用。先读磁盘缓存（同步、很快），必要时在后台拉取。
    /// 重复调用同一年份不会重复发起请求。
    /// </summary>
    public void EnsureYear(int year)
    {
        lock (_sync)
        {
            if (_memory.ContainsKey(year) || _inFlight.Contains(year))
            {
                return;
            }
        }

        var cache = ReadCache(year);
        if (cache is not null)
        {
            Apply(year, cache);

            if (!NeedsRefresh(cache))
            {
                return;
            }
        }

        lock (_sync)
        {
            if (!_inFlight.Add(year))
            {
                return;
            }
        }

        _ = FetchAsync(year);
    }

    private static bool NeedsRefresh(HolidayYearCache cache)
    {
        // 往年数据不会再变；当年数据过一段时间重拉一次。
        return cache.Year >= DateTime.Today.Year
            && DateTime.Now - cache.FetchedAt > CurrentYearCacheLifetime;
    }

    private async Task FetchAsync(int year)
    {
        try
        {
            var cache = await FetchFromTimorAsync(year) ?? await FetchFromGithubAsync(year);
            if (cache is null)
            {
                Log.Warn($"{year} 年的法定假日数据拉取失败（两个数据源均不可用），本次不显示假日角标。");
                return;
            }

            WriteCache(cache);
            Apply(year, cache);
            Log.Info($"{year} 年的法定假日数据已更新，来源 {cache.Source}，共 {cache.Days.Count} 天。");
        }
        catch (Exception ex)
        {
            Log.Warn($"{year} 年的法定假日数据拉取异常。", ex);
        }
        finally
        {
            lock (_sync)
            {
                _inFlight.Remove(year);
            }
        }
    }

    /// <summary>国内源，格式：holiday 映射表，键为 MM-DD，holiday=false 表示调休上班。</summary>
    private static async Task<HolidayYearCache?> FetchFromTimorAsync(int year)
    {
        try
        {
            var json = await Http.GetStringAsync($"https://timor.tech/api/holiday/year/{year}");
            var payload = JsonSerializer.Deserialize<TimorResponse>(json, JsonOptions);
            if (payload?.Holiday is null || payload.Holiday.Count == 0)
            {
                return null;
            }

            var cache = new HolidayYearCache
            {
                Year = year,
                FetchedAt = DateTime.Now,
                Source = "timor.tech"
            };

            foreach (var (_, day) in payload.Holiday)
            {
                if (string.IsNullOrWhiteSpace(day.Date))
                {
                    continue;
                }

                cache.Days[day.Date] = new HolidayDay { Name = day.Name, IsOffDay = day.Holiday };
            }

            return cache.Days.Count > 0 ? cache : null;
        }
        catch (Exception ex)
        {
            Log.Info($"国内假日源不可用（{ex.GetType().Name}），改用备用源。");
            return null;
        }
    }

    /// <summary>备用源，格式：days 数组，isOffDay=false 表示调休上班；附国务院公告链接。</summary>
    private static async Task<HolidayYearCache?> FetchFromGithubAsync(int year)
    {
        try
        {
            var json = await Http.GetStringAsync(
                $"https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/{year}.json");
            var payload = JsonSerializer.Deserialize<HolidayCnResponse>(json, JsonOptions);
            if (payload?.Days is null || payload.Days.Count == 0)
            {
                return null;
            }

            var cache = new HolidayYearCache
            {
                Year = year,
                FetchedAt = DateTime.Now,
                Source = "holiday-cn"
            };

            foreach (var day in payload.Days)
            {
                if (string.IsNullOrWhiteSpace(day.Date))
                {
                    continue;
                }

                cache.Days[day.Date] = new HolidayDay { Name = day.Name, IsOffDay = day.IsOffDay };
            }

            return cache.Days.Count > 0 ? cache : null;
        }
        catch (Exception ex)
        {
            Log.Warn("备用假日源也不可用。", ex);
            return null;
        }
    }

    private void Apply(int year, HolidayYearCache cache)
    {
        var parsed = new Dictionary<DateTime, HolidayInfo>();
        foreach (var (key, day) in cache.Days)
        {
            if (Models.DateKeys.ParseDateKey(key) is { } date)
            {
                parsed[date] = new HolidayInfo(day.Name, day.IsOffDay);
            }
        }

        lock (_sync)
        {
            _memory[year] = parsed;
        }

        YearLoaded?.Invoke(year);
    }

    private string CachePath(int year) => Path.Combine(_cacheDirectory, $"{year}.json");

    private HolidayYearCache? ReadCache(int year)
    {
        var path = CachePath(year);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<HolidayYearCache>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            Log.Warn($"假日缓存 {path} 解析失败，将重新拉取。", ex);
            return null;
        }
    }

    private void WriteCache(HolidayYearCache cache)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var path = CachePath(cache.Year);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(cache, JsonOptions));

            if (File.Exists(path))
            {
                File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        catch (Exception ex)
        {
            // 缓存写不下去只影响下次启动要重拉，不影响本次显示。
            Log.Warn("假日缓存写入失败。", ex);
        }
    }

    private sealed class TimorResponse
    {
        [JsonPropertyName("holiday")]
        public Dictionary<string, TimorDay>? Holiday { get; set; }
    }

    private sealed class TimorDay
    {
        [JsonPropertyName("holiday")]
        public bool Holiday { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;
    }

    private sealed class HolidayCnResponse
    {
        [JsonPropertyName("days")]
        public List<HolidayCnDay>? Days { get; set; }
    }

    private sealed class HolidayCnDay
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;

        [JsonPropertyName("isOffDay")]
        public bool IsOffDay { get; set; }
    }
}
