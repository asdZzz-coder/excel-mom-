using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace ServiceRecord.Services
{
    /// <summary>
    /// 字體（整個畫面）放大縮小。每個視窗最外層用 LayoutTransform="{DynamicResource UiScale}"，
    /// 切換時把 Application.Resources 裡的 UiScale 換成新的倍率，所有視窗即時跟著縮放。
    /// 倍率存在資料資料夾的 zoom.txt。
    /// </summary>
    public static class ZoomService
    {
        public const double Min = 0.8;
        public const double Max = 2.0;
        public const double Step = 0.1;

        private static string SettingFile => Path.Combine(DataStore.DataDirectory, "zoom.txt");

        public static double Scale { get; private set; } = 1.0;

        /// <summary>例如 120（%）。</summary>
        public static int Percent => (int)Math.Round(Scale * 100);

        /// <summary>倍率套用後觸發，讓主畫面更新百分比文字。</summary>
        public static event Action? ScaleChanged;

        // ---------- 讀取 / 儲存 ----------

        /// <summary>讀取使用者上次的倍率；沒有或看不懂時為 100%。</summary>
        public static void Load()
        {
            string? saved = null;
            try { if (File.Exists(SettingFile)) saved = File.ReadAllText(SettingFile); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Scale = Parse(saved);
        }

        internal static double Parse(string? text) =>
            double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)
                ? Clamp(v)
                : 1.0;

        internal static string ToSettingText(double scale) => scale.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>限制在 80%～200%，並去掉 0.1 累加的誤差（0.9000000001 → 0.9）。</summary>
        internal static double Clamp(double scale) => Math.Round(Math.Clamp(scale, Min, Max), 2);

        public static bool CanZoomIn => Scale < Max;
        public static bool CanZoomOut => Scale > Min;

        public static void ZoomIn() => SetScale(Scale + Step);
        public static void ZoomOut() => SetScale(Scale - Step);
        public static void Reset() => SetScale(1.0);

        public static void SetScale(double scale)
        {
            scale = Clamp(scale);
            if (scale == Scale) return;
            Scale = scale;
            try
            {
                Directory.CreateDirectory(DataStore.DataDirectory);
                File.WriteAllText(SettingFile, ToSettingText(scale));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Apply();
        }

        // ---------- 套用 ----------

        /// <summary>把目前的倍率寫進 Application.Resources（App.xaml 載入後呼叫）。</summary>
        public static void Apply()
        {
            var transform = new ScaleTransform(Scale, Scale);
            transform.Freeze();
            Application.Current.Resources["UiScale"] = transform;
            ScaleChanged?.Invoke();
        }

        /// <summary>對話框打開時，視窗大小跟著倍率放大，內容才放得下（不超過螢幕可用範圍）。</summary>
        public static void FitWindow(Window window)
        {
            var area = SystemParameters.WorkArea;
            double maxW = area.Width - 20, maxH = area.Height - 20;
            window.MinWidth = Math.Min(window.MinWidth * Scale, maxW);
            window.MinHeight = Math.Min(window.MinHeight * Scale, maxH);
            if (!double.IsNaN(window.Width)) window.Width = Math.Min(window.Width * Scale, maxW);
            if (!double.IsNaN(window.Height)) window.Height = Math.Min(window.Height * Scale, maxH);
        }
    }
}
