using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace ServiceRecord.Services
{
    /// <summary>
    /// 「居服紀錄表」的捷徑（.lnk）。安裝版的程式固定放在同一個資料夾，更新也不換路徑，
    /// 所以捷徑和工作列釘選建立一次就一直有效。
    /// - 開始功能表：安裝版每次開啟時確認存在、指向目前的程式。
    /// - 桌面：用「安裝.cmd」安裝完第一次開啟時建立；之後使用者自己刪掉就不再加回來，要的話按「桌面捷徑」重建。
    /// </summary>
    public static class ShortcutService
    {
        public const string LinkName = "居服紀錄表.lnk";
        internal const string DevLinkName = "居服紀錄表 (開發版).lnk";

        // 環境變數可指定其他資料夾當作桌面 / 開始功能表（測試用）；平常不設定，桌面可能在 OneDrive 底下，一定要用系統回報的實際路徑
        public static string DesktopDirectory =>
            Environment.GetEnvironmentVariable("SERVICERECORD_DESKTOP_DIR")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        public static string StartMenuDirectory =>
            Environment.GetEnvironmentVariable("SERVICERECORD_STARTMENU_DIR")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);

        /// <summary>建立或重建桌面捷徑：桌面上已經有指向這個程式的捷徑（可能被改過名字）就覆蓋它，不另外多放一個。</summary>
        public static string CreateDesktop(bool isInstalled, string exePath) => CreateDesktop(isInstalled, exePath, DesktopDirectory);

        internal static string CreateDesktop(bool isInstalled, string exePath, string desktop)
        {
            Directory.CreateDirectory(desktop);
            var target = FindLinksTo(desktop, exePath).FirstOrDefault()
                         ?? Path.Combine(desktop, isInstalled ? LinkName : DevLinkName);
            WriteLink(target, exePath);
            return target;
        }

        /// <summary>桌面上還沒有指向這個程式的捷徑才建立（安裝完第一次開啟時用）。</summary>
        public static void EnsureDesktop(string exePath) => EnsureDesktop(exePath, DesktopDirectory);

        internal static void EnsureDesktop(string exePath, string desktop)
        {
            if (!FindLinksTo(desktop, exePath).Any()) CreateDesktop(true, exePath, desktop);
        }

        /// <summary>開始功能表的捷徑不存在或指向別的地方時重建。</summary>
        public static void EnsureStartMenu(string exePath) => EnsureStartMenu(exePath, StartMenuDirectory);

        internal static void EnsureStartMenu(string exePath, string programs)
        {
            var link = Path.Combine(programs, LinkName);
            if (File.Exists(link) && SamePath(ReadLink(link), exePath)) return;
            Directory.CreateDirectory(programs);
            WriteLink(link, exePath);
        }

        /// <summary>解除安裝：刪掉開始功能表和桌面上指向這個程式的捷徑（其他程式的捷徑不碰）。</summary>
        public static void RemoveAll(string exePath) => RemoveAll(exePath, DesktopDirectory, StartMenuDirectory);

        internal static void RemoveAll(string exePath, string desktop, string programs)
        {
            foreach (var link in FindLinksTo(desktop, exePath).Concat(FindLinksTo(programs, exePath)).ToList())
                File.Delete(link);
        }

        private static IEnumerable<string> FindLinksTo(string folder, string exePath)
        {
            if (!Directory.Exists(folder)) return [];
            var result = new List<string>();
            foreach (var link in Directory.EnumerateFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly))
            {
                try { if (SamePath(ReadLink(link), exePath)) result.Add(link); }
                catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException) { /* 壞掉的捷徑略過 */ }
            }
            return result;
        }

        /// <summary>
        /// 兩個路徑是否指向同一個檔案。有些捷徑不是指向檔案（例如 Windows 內建的特殊捷徑），
        /// 讀出來的路徑是空的，直接當作不同。
        /// </summary>
        internal static bool SamePath(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        }

        // ---------- .lnk（Windows Shell 的 IShellLink） ----------

        internal static void WriteLink(string linkPath, string targetPath)
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetDescription("居服紀錄表");
                link.SetPath(targetPath);
                link.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? "");
                link.SetIconLocation(targetPath, 0);
                ((IPersistFile)link).Save(linkPath, true);
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }

        /// <summary>讀出 .lnk 指向的檔案。</summary>
        internal static string ReadLink(string linkPath)
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistFile)link).Load(linkPath, 0);
                var sb = new StringBuilder(1024);
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                return sb.ToString();
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }
}
