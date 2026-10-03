using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ServiceRecord.Services
{
    /// <summary>
    /// 安裝版的位置與登記。「安裝.cmd」只負責把 app 資料夾複製到 %LOCALAPPDATA%\Programs\ServiceRecord，
    /// 其餘（開始功能表、桌面捷徑、登記到「設定 → 應用程式」以便解除安裝）由程式開啟時自己處理。
    /// 不用 ClickOnce：ClickOnce 一定要搭配每個程式各自產生、沒有簽章的 Launcher.exe，
    /// 開著「智慧型應用程式控制」的電腦會擋下它而裝不起來。
    /// </summary>
    public static class InstallService
    {
        public const string ExeName = "ServiceRecord.exe";

        /// <summary>安裝.cmd 安裝完開啟程式時帶的參數：第一次開啟要建立桌面捷徑。</summary>
        public const string InstalledArg = "--installed";
        /// <summary>「設定 → 應用程式」按解除安裝時執行的參數。</summary>
        public const string UninstallArg = "--uninstall";

        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ServiceRecord";

        // 環境變數 SERVICERECORD_INSTALL_DIR 可指定其他資料夾（測試用）
        public static string InstallDirectory =>
            Environment.GetEnvironmentVariable("SERVICERECORD_INSTALL_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ServiceRecord");

        public static string InstalledExe => Path.Combine(InstallDirectory, ExeName);

        /// <summary>目前執行的是不是安裝版（從安裝資料夾執行）。直接從 Visual Studio / dotnet run 執行時為 false。</summary>
        public static bool IsInstalled => IsInside(AppContext.BaseDirectory, InstallDirectory);

        internal static bool IsInside(string baseDirectory, string installDirectory) =>
            string.Equals(Path.GetFullPath(baseDirectory).TrimEnd('\\'), Path.GetFullPath(installDirectory).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);

        /// <summary>安裝版每次開啟：補開始功能表捷徑、更新「應用程式」清單裡的版本；剛用安裝.cmd 裝好時再建立桌面捷徑。</summary>
        public static void OnStartup(string[] args, Version version)
        {
            if (!IsInstalled) return;
            // 這些都是附帶的整理工作，任何一項失敗都不該讓程式打不開，下次開啟再試
            TryRun(() => ShortcutService.EnsureStartMenu(InstalledExe));
            if (args.Contains(InstalledArg, StringComparer.OrdinalIgnoreCase))
                TryRun(() => ShortcutService.EnsureDesktop(InstalledExe));
            TryRun(() => Register(version));
        }

        private static void TryRun(Action action)
        {
            try { action(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                           or System.Runtime.InteropServices.COMException or System.Security.SecurityException) { }
        }

        /// <summary>登記到「設定 → 應用程式」（只寫目前使用者，不需要系統管理員權限）。</summary>
        private static void Register(Version version)
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
            key.SetValue("DisplayName", "居服紀錄表");
            key.SetValue("DisplayVersion", version.ToString());
            key.SetValue("Publisher", "asdZzz-coder");
            key.SetValue("DisplayIcon", InstalledExe);
            key.SetValue("InstallLocation", InstallDirectory);
            key.SetValue("UninstallString", $"\"{InstalledExe}\" {UninstallArg}");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", (int)(DirectorySize(InstallDirectory) / 1024), RegistryValueKind.DWord);
        }

        /// <summary>
        /// 解除安裝：刪捷徑、刪登記，程式結束後再刪掉安裝資料夾。
        /// 使用者的紀錄（%AppData%\ServiceRecord）保留，重新安裝後會繼續使用。
        /// </summary>
        public static void Uninstall()
        {
            ShortcutService.RemoveAll(InstalledExe);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

            // 程式自己還在執行，資料夾刪不掉：交給一個隱藏的 cmd 等幾秒再刪
            Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c ping 127.0.0.1 -n 4 >nul & rmdir /s /q \"{InstallDirectory}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(),
            });
        }

        private static long DirectorySize(string dir)
        {
            try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
        }
    }
}
