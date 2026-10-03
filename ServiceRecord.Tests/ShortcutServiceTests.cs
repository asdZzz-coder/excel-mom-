using System.IO;
using ServiceRecord.Services;

namespace ServiceRecord.Tests
{
    /// <summary>
    /// 捷徑與安裝路徑的自動測試。全部在暫存資料夾裡模擬「桌面」與「開始功能表」，不會動到真正的桌面。
    /// </summary>
    public sealed class ShortcutServiceTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ServiceRecord-ShortcutTests-" + Guid.NewGuid().ToString("N"));
        private string Desktop => Path.Combine(_root, "Desktop");
        private string Programs => Path.Combine(_root, "Programs");

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private string FakeExe(string folder = "Install")
        {
            var exe = Path.Combine(_root, folder, "ServiceRecord.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllBytes(exe, [0x4D, 0x5A]); // 只需要檔案存在
            return exe;
        }

        private string[] Files(string folder) =>
            Directory.Exists(folder) ? Directory.GetFiles(folder).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray() : [];

        [Fact]
        public void CreateDesktop_Installed_PointsToExe()
        {
            var exe = FakeExe();

            var link = ShortcutService.CreateDesktop(true, exe, Desktop);

            Assert.Equal([ShortcutService.LinkName], Files(Desktop));
            Assert.Equal(exe, ShortcutService.ReadLink(link), ignoreCase: true);
        }

        [Fact]
        public void CreateDesktop_DevBuild_UsesDevName()
        {
            ShortcutService.CreateDesktop(false, FakeExe("bin"), Desktop);
            Assert.Equal([ShortcutService.DevLinkName], Files(Desktop));
        }

        [Fact]
        public void CreateDesktop_Twice_DoesNotDuplicate_AndKeepsRenamedShortcut()
        {
            var exe = FakeExe();
            var renamed = Path.Combine(Desktop, "媽媽的紀錄表.lnk");
            Directory.CreateDirectory(Desktop);
            ShortcutService.WriteLink(renamed, exe);

            ShortcutService.CreateDesktop(true, exe, Desktop);
            ShortcutService.CreateDesktop(true, exe, Desktop);

            Assert.Equal(["媽媽的紀錄表.lnk"], Files(Desktop));
        }

        [Fact]
        public void EnsureDesktop_OnlyWhenMissing()
        {
            var exe = FakeExe();
            ShortcutService.EnsureDesktop(exe, Desktop);
            var stamp = DateTime.Now.AddDays(-1);
            File.SetLastWriteTime(Path.Combine(Desktop, ShortcutService.LinkName), stamp);

            ShortcutService.EnsureDesktop(exe, Desktop);

            Assert.Equal(stamp, File.GetLastWriteTime(Path.Combine(Desktop, ShortcutService.LinkName)));
        }

        [Fact]
        public void EnsureStartMenu_CreatesAndFixesWrongTarget()
        {
            var exe = FakeExe();
            var link = Path.Combine(Programs, ShortcutService.LinkName);
            Directory.CreateDirectory(Programs);
            ShortcutService.WriteLink(link, FakeExe("Old"));

            ShortcutService.EnsureStartMenu(exe, Programs);

            Assert.Equal(exe, ShortcutService.ReadLink(link), ignoreCase: true);
        }

        [Fact]
        public void RemoveAll_RemovesOnlyOurShortcuts()
        {
            var exe = FakeExe();
            var other = FakeExe("Other");
            ShortcutService.CreateDesktop(true, exe, Desktop);
            ShortcutService.EnsureStartMenu(exe, Programs);
            ShortcutService.WriteLink(Path.Combine(Desktop, "別的程式.lnk"), other);

            ShortcutService.RemoveAll(exe, Desktop, Programs);

            Assert.Equal(["別的程式.lnk"], Files(Desktop));
            Assert.Empty(Files(Programs));
        }

        [Fact]
        public void RemoveAll_IgnoresShortcutsWithoutFileTarget()
        {
            // 開始功能表裡有些捷徑不是指向檔案（讀出來的路徑是空的），不能因此失敗
            var exe = FakeExe();
            ShortcutService.EnsureStartMenu(exe, Programs);
            ShortcutService.WriteLink(Path.Combine(Programs, "特殊捷徑.lnk"), "");

            ShortcutService.RemoveAll(exe, Desktop, Programs);

            Assert.Equal(["特殊捷徑.lnk"], Files(Programs));
        }

        [Theory]
        [InlineData("", @"C:\x\ServiceRecord.exe", false)]
        [InlineData(@"C:\x\ServiceRecord.exe", "", false)]
        [InlineData(@"C:\X\servicerecord.EXE", @"C:\x\ServiceRecord.exe", true)]
        public void SamePath_HandlesEmptyPaths(string a, string b, bool expected)
        {
            Assert.Equal(expected, ShortcutService.SamePath(a, b));
        }

        [Theory]
        [InlineData(@"C:\Users\a\AppData\Local\Programs\ServiceRecord\", @"C:\Users\a\AppData\Local\Programs\ServiceRecord", true)]
        [InlineData(@"c:\users\a\appdata\local\programs\servicerecord", @"C:\Users\a\AppData\Local\Programs\ServiceRecord", true)]
        [InlineData(@"C:\vscode studio\excel(mom)\ServiceRecord\bin\Debug\net10.0-windows\", @"C:\Users\a\AppData\Local\Programs\ServiceRecord", false)]
        [InlineData(@"C:\Users\a\AppData\Local\Programs\ServiceRecord\sub\", @"C:\Users\a\AppData\Local\Programs\ServiceRecord", false)]
        public void IsInstalled_ComparesRunningFolderWithInstallFolder(string baseDir, string installDir, bool expected)
        {
            Assert.Equal(expected, InstallService.IsInside(baseDir, installDir));
        }
    }
}
