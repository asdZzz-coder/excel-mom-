using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServiceRecord.Services
{
    /// <summary>
    /// 台灣的放假日（國定假日、補假、週末），用來把日期標成紅色。
    /// 資料是「中華民國政府行政機關辦公日曆表」（人事行政總處公告），JSON 版本來自
    /// github.com/ruyut/TaiwanCalendar。依序使用：
    /// 1. 下載過的檔案（%AppData%\ServiceRecord\holidays\2026.json，每 30 天重新下載一次）
    /// 2. 程式內附的年份（Holidays\2026.json 等，沒有網路也能用）
    /// 3. 都沒有時（例如還沒公告的年份）：週末＋自己算的國定假日（沒有補假）
    /// </summary>
    public static class HolidayService
    {
        private const string SourceUrl = "https://cdn.jsdelivr.net/gh/ruyut/TaiwanCalendar/data/{0}.json";
        private static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(30);

        public sealed record Day(bool IsOff, string Name);

        /// <summary>某一年的日曆。Official = 官方日曆；false 表示是自己算的（可能少了補假）。</summary>
        public sealed record YearCalendar(int Year, IReadOnlyDictionary<DateOnly, Day> Days, bool Official);

        private static readonly ConcurrentDictionary<int, YearCalendar> Years = new();
        private static readonly ConcurrentDictionary<int, byte> Refreshing = new();

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("ServiceRecord");
            return c;
        }

        /// <summary>下載到新的日曆後觸發（參數是年份），畫面要重畫日期。可能在背景執行緒觸發。</summary>
        public static event Action<int>? YearUpdated;

        private static string CacheDirectory => Path.Combine(DataStore.DataDirectory, "holidays");
        private static string CachePath(int year) => Path.Combine(CacheDirectory, $"{year}.json");

        // ---------- 查詢 ----------

        /// <summary>這天要不要標紅（放假）。官方日曆有補行上班的週六會是 false。</summary>
        public static bool IsOffDay(DateOnly date) =>
            Get(date.Year).Days.TryGetValue(date, out var day) ? day.IsOff : IsWeekend(date);

        /// <summary>這天的節日名稱（例如「端午節」「補假」）；一般週末或平日為 null。</summary>
        public static string? NameOf(DateOnly date) =>
            Get(date.Year).Days.TryGetValue(date, out var day) && day.Name.Length > 0 ? day.Name : null;

        /// <summary>這個月有名稱的放假日（給畫面列出「本月國定假日」）。</summary>
        public static IReadOnlyList<(DateOnly Date, string Name)> NamedHolidaysIn(int year, int month)
        {
            var list = new List<(DateOnly, string)>();
            for (var d = new DateOnly(year, month, 1); d.Month == month; d = d.AddDays(1))
                if (IsOffDay(d) && NameOf(d) is { } name) list.Add((d, name));
            return list;
        }

        public static bool IsOfficial(int year) => Get(year).Official;

        private static bool IsWeekend(DateOnly d) => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        public static YearCalendar Get(int year) => Years.GetOrAdd(year, Load);

        // ---------- 載入 ----------

        private static YearCalendar Load(int year)
        {
            try
            {
                if (File.Exists(CachePath(year)) && Parse(File.ReadAllText(CachePath(year)), year) is { } cached)
                    return cached;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"ServiceRecord.Holidays.{year}.json");
            if (stream != null && Parse(new StreamReader(stream).ReadToEnd(), year) is { } bundled)
                return bundled;

            return Computed(year);
        }

        private sealed class JsonDay
        {
            [JsonPropertyName("date")] public string? Date { get; set; }
            [JsonPropertyName("isHoliday")] public bool IsHoliday { get; set; }
            [JsonPropertyName("description")] public string? Description { get; set; }
        }

        /// <summary>讀官方日曆 JSON。內容不完整（不是整年、年份不對、格式錯）就回傳 null，不採用。</summary>
        internal static YearCalendar? Parse(string json, int year)
        {
            List<JsonDay>? items;
            try { items = JsonSerializer.Deserialize<List<JsonDay>>(json); }
            catch (JsonException) { return null; }
            if (items == null) return null;

            var days = new Dictionary<DateOnly, Day>();
            foreach (var item in items)
            {
                if (!DateOnly.TryParseExact(item.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || d.Year != year)
                    return null;
                days[d] = new Day(item.IsHoliday, item.Description?.Trim() ?? "");
            }
            return days.Count == (DateTime.IsLeapYear(year) ? 366 : 365) ? new YearCalendar(year, days, true) : null;
        }

        // ---------- 沒有官方日曆時自己算 ----------

        /// <summary>
        /// 週末＋國定假日（依 2025 年修正的《紀念日及節日實施條例》）。農曆節日用 .NET 的台灣農曆換算。
        /// 沒有補假和調整放假，所以只在沒有官方日曆時使用。
        /// </summary>
        internal static YearCalendar Computed(int year)
        {
            var days = new Dictionary<DateOnly, Day>();
            for (var d = new DateOnly(year, 1, 1); d.Year == year; d = d.AddDays(1))
                if (IsWeekend(d)) days[d] = new Day(true, "");

            void Add(DateOnly d, string name)
            {
                if (d.Year == year) days[d] = new Day(true, name);
            }

            Add(new DateOnly(year, 1, 1), "開國紀念日");
            Add(new DateOnly(year, 2, 28), "和平紀念日");
            Add(new DateOnly(year, 4, 4), "兒童節");
            Add(QingMing(year), "清明節");
            Add(new DateOnly(year, 5, 1), "勞動節");
            Add(new DateOnly(year, 9, 28), "孔子誕辰紀念日/教師節");
            Add(new DateOnly(year, 10, 10), "國慶日");
            Add(new DateOnly(year, 10, 25), "臺灣光復暨金門古寧頭大捷紀念日");
            Add(new DateOnly(year, 12, 25), "行憲紀念日");

            if (Lunar(year, 1, 1) is { } newYear)
            {
                Add(newYear.AddDays(-2), "小年夜");
                Add(newYear.AddDays(-1), "農曆除夕");
                for (int i = 0; i < 3; i++) Add(newYear.AddDays(i), "春節");
            }
            if (Lunar(year, 5, 5) is { } dragon) Add(dragon, "端午節");
            if (Lunar(year, 8, 15) is { } moon) Add(moon, "中秋節");

            return new YearCalendar(year, days, false);
        }

        /// <summary>農曆某月某日在這一年的國曆日期（.NET 支援的範圍外回傳 null）。</summary>
        internal static DateOnly? Lunar(int year, int month, int day)
        {
            var cal = new TaiwanLunisolarCalendar();
            int lunarYear = year - 1911; // 民國年
            try
            {
                // 有閏月時，閏月及之後的月份編號要往後一個
                int leap = cal.GetLeapMonth(lunarYear);
                int m = leap > 0 && leap <= month ? month + 1 : month;
                return DateOnly.FromDateTime(cal.ToDateTime(lunarYear, m, day, 0, 0, 0, 0));
            }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        /// <summary>清明（節氣），4 月 4 或 5 日。21 世紀的近似公式。</summary>
        internal static DateOnly QingMing(int year)
        {
            int y = year % 100;
            int day = (int)(y * 0.2422 + 4.81) - y / 4;
            return new DateOnly(year, 4, Math.Clamp(day, 4, 6));
        }

        // ---------- 下載最新的官方日曆 ----------

        /// <summary>
        /// 背景下載這一年的官方日曆（下載過且 30 天內就不再下載；這次執行已試過也不再試）。
        /// 失敗（沒網路、還沒公告）就繼續用原本的資料。
        /// </summary>
        public static async Task RefreshAsync(int year)
        {
            if (!Refreshing.TryAdd(year, 0)) return;
            try
            {
                var path = CachePath(year);
                if (File.Exists(path) && DateTime.Now - File.GetLastWriteTime(path) < RefreshAfter) return;

                var json = await Http.GetStringAsync(string.Format(CultureInfo.InvariantCulture, SourceUrl, year)).ConfigureAwait(false);
                if (Parse(json, year) is not { } calendar) return;

                Directory.CreateDirectory(CacheDirectory);
                var tmp = path + ".tmp";
                await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
                File.Move(tmp, path, overwrite: true);

                var old = Years.TryGetValue(year, out var o) ? o : null;
                Years[year] = calendar;
                if (old == null || !SameOffDays(old, calendar)) YearUpdated?.Invoke(year);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException) { }
        }

        private static bool SameOffDays(YearCalendar a, YearCalendar b)
        {
            for (var d = new DateOnly(a.Year, 1, 1); d.Year == a.Year; d = d.AddDays(1))
            {
                var da = a.Days.TryGetValue(d, out var x) ? x : new Day(IsWeekend(d), "");
                var db = b.Days.TryGetValue(d, out var y) ? y : new Day(IsWeekend(d), "");
                if (da.IsOff != db.IsOff || da.Name != db.Name) return false;
            }
            return true;
        }

        /// <summary>測試用：清掉記住的年份，下次查詢重新載入。</summary>
        internal static void ClearCache()
        {
            Years.Clear();
            Refreshing.Clear();
        }
    }
}
