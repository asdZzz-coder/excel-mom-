using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using ServiceRecord.Services;

namespace ServiceRecord
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // 只允許開一個視窗，避免兩個視窗互相覆蓋對方存的資料（以資料資料夾區分：測試用的資料夾不會擋到平常用的）
            using var mutex = new Mutex(true, SingleInstanceName(DataStore.DataDirectory), out bool isFirst);
            if (!isFirst)
            {
                MessageBox.Show("居服紀錄表已經開著了。", "居服紀錄表");
                return;
            }

            if (args.Contains(InstallService.UninstallArg, StringComparer.OrdinalIgnoreCase))
            {
                Uninstall();
                return;
            }

            // 安裝版：補開始功能表捷徑、更新「應用程式」清單；剛安裝完再建立桌面捷徑
            InstallService.OnStartup(args, new UpdateService().Version);

            ThemeService.Load(); // 上次選的外觀（跟隨系統 / 淺色 / 深色）
            ZoomService.Load();  // 上次的字體大小

            var app = new App();
            app.InitializeComponent();
            ThemeService.Apply();
            ThemeService.WatchSystemTheme();
            ZoomService.Apply();
            app.Run();
        }

        /// <summary>同一個資料資料夾只能開一個視窗。Mutex 名稱不能有「\」，所以用路徑的雜湊。</summary>
        private static string SingleInstanceName(string dataDirectory)
        {
            var path = Path.GetFullPath(dataDirectory).TrimEnd('\\').ToUpperInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..16];
            return $@"Local\ServiceRecord.SingleInstance.{hash}";
        }

        /// <summary>從「設定 → 應用程式」按解除安裝時執行。</summary>
        private static void Uninstall()
        {
            const string title = "解除安裝 居服紀錄表";
            if (!InstallService.IsInstalled)
            {
                MessageBox.Show("這不是安裝版的居服紀錄表，不需要解除安裝。", title);
                return;
            }

            var ok = MessageBox.Show(
                "確定要解除安裝「居服紀錄表」嗎？\n\n你填過的紀錄會保留在電腦裡，之後重新安裝會繼續使用。",
                title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (ok != MessageBoxResult.Yes) return;

            try
            {
                InstallService.Uninstall();
                MessageBox.Show(
                    $"已解除安裝。\n\n紀錄保留在：\n{DataStore.DataDirectory}",
                    title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"解除安裝失敗：{ex.Message}", title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
