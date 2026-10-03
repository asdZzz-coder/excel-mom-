using System.Windows;
using ServiceRecord.Services;

namespace ServiceRecord
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // 只允許開一個視窗，避免兩個視窗互相覆蓋對方存的資料
            using var mutex = new Mutex(true, @"Local\ServiceRecord.SingleInstance", out bool isFirst);
            if (!isFirst)
            {
                MessageBox.Show("居服紀錄表已經開著了。", "居服紀錄表");
                return;
            }

            // 安裝版使用固定的工作列身分，更新後新版視窗才會跟工作列釘選合併
            if (new UpdateService().IsInstalled) DesktopShortcutService.ApplyAppId();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
