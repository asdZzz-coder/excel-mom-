using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using ServiceRecord.Models;

namespace ServiceRecord.Services
{
    /// <summary>
    /// 資料存在 %AppData%\ServiceRecord：
    ///   settings.json      設定（居服員姓名、比例、預設個案與服務項目）
    ///   months\2026-06.json 每個月一個檔
    /// 一律先寫 .tmp 再換名，寫到一半當機也不會把舊檔弄壞。
    /// </summary>
    public static class DataStore
    {
        // 環境變數 SERVICERECORD_DATA_DIR 可指定其他資料夾（測試用）；平常不設定，存在 %AppData%\ServiceRecord
        public static string DataDirectory { get; set; } =
            Environment.GetEnvironmentVariable("SERVICERECORD_DATA_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ServiceRecord");

        public static string MonthsDirectory => Path.Combine(DataDirectory, "months");

        private static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

        private static string MonthPath(int year, int month) => Path.Combine(MonthsDirectory, MonthRecord.Format(year, month) + ".json");

        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文直接寫，不轉成 \uXXXX，方便必要時打開來看
        };

        public static AppSettings LoadSettings() => Read<AppSettings>(SettingsPath) ?? new AppSettings();

        public static void SaveSettings(AppSettings settings) => Write(SettingsPath, settings);

        /// <summary>讀取某月的紀錄；還沒有存過則回傳 null。</summary>
        public static MonthRecord? LoadMonth(int year, int month) => Read<MonthRecord>(MonthPath(year, month));

        public static bool MonthExists(int year, int month) => File.Exists(MonthPath(year, month));

        public static void SaveMonth(MonthRecord record) => Write(MonthPath(record.Year, record.Month), record);

        private static T? Read<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
            }
            catch (JsonException)
            {
                // 檔案壞掉（例如被手動改錯）：備份起來，當作沒有這個檔
                File.Move(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                return null;
            }
        }

        private static void Write<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
            File.Move(tmp, path, overwrite: true);
        }
    }
}
