using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace ServiceRecord.Services
{
    public record UpdateInfo(Version Version, string DownloadUrl, long Size);

    /// <summary>
    /// 線上更新：向 GitHub Releases 查詢最新版，使用者同意後下載安裝包（zip）、解壓，
    /// 執行裡面的「安裝.cmd /update」。安裝.cmd 會等本程式結束，把新版複製到安裝資料夾後重新開啟。
    /// 只有「安裝版」才能更新；直接從 Visual Studio / dotnet run 執行時 IsInstalled 為 false，會略過。
    /// </summary>
    public class UpdateService
    {
        private const string Owner = "asdZzz-coder";
        private const string Repo = "excel-mom-";
        public const string PackageAssetName = "ServiceRecord-Setup.zip";
        private const string InstallScript = "安裝.cmd";

        /// <summary>下載與解壓更新包的資料夾（啟動時由 CleanupService 清掉）。</summary>
        public static readonly string DownloadFolder = Path.Combine(Path.GetTempPath(), "ServiceRecord-Update");

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("ServiceRecord-Updater"); // GitHub API 要求有 User-Agent
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        public bool IsInstalled => InstallService.IsInstalled;

        /// <summary>程式本身的版本（建置時由 -p:Version 帶入）。</summary>
        public Version Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            }
        }

        public string CurrentVersion => IsInstalled ? Version.ToString() : "開發版";

        /// <summary>檢查是否有新版；沒有、或非安裝版則回傳 null。</summary>
        public async Task<UpdateInfo?> CheckAsync()
        {
            if (!IsInstalled) return null;

            using var resp = await Http.GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
            if (latest <= Version) return null;

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() == PackageAssetName)
                    return new UpdateInfo(latest, asset.GetProperty("browser_download_url").GetString()!, asset.GetProperty("size").GetInt64());
            }
            return null; // 該版本還沒有附上安裝包（打包尚未完成）
        }

        /// <summary>下載新版安裝包、解壓並啟動安裝.cmd；呼叫端應在這之後結束程式。</summary>
        public async Task DownloadAndLaunchAsync(UpdateInfo info, Action<int>? progress = null)
        {
            // 先清掉先前（例如失敗或中斷的更新）遺留的檔案
            TryDeleteDirectory(DownloadFolder);
            Directory.CreateDirectory(DownloadFolder);
            var zipPath = Path.Combine(DownloadFolder, $"ServiceRecord-{info.Version}.zip");
            var extractDir = Path.Combine(DownloadFolder, info.Version.ToString());

            using (var resp = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? info.Size;
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(zipPath);
                var buf = new byte[81920];
                long done = 0;
                int last = -1, n;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    done += n;
                    int pct = total > 0 ? (int)(done * 100 / total) : 0;
                    if (pct != last) { last = pct; progress?.Invoke(pct); }
                }
            }

            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractDir));
            var script = Path.Combine(extractDir, InstallScript);
            if (!File.Exists(script) || !File.Exists(Path.Combine(extractDir, "app", InstallService.ExeName)))
                throw new FileNotFoundException("更新檔內容不完整，請稍後再試。", InstallScript);

            Process.Start(new ProcessStartInfo(script, "/update") { UseShellExecute = true, WorkingDirectory = extractDir });
        }

        internal static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 被占用，下次再清 */ }
        }
    }
}
