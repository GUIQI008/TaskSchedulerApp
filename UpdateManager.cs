using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using MessageBox = HandyControl.Controls.MessageBox;

public class UpdateManager
{
    public const string CurrentVersion = "2.2.3";

    // 【必改】替换为你的 GitHub 用户名和仓库名
    private const string RepoOwner = "GUIQI008";
    private const string RepoName = "TaskSchedulerApp";

    public static async Task CheckForUpdatesManual(Action<string> onProgress)
    {
        try
        {
            using var client = new HttpClient();
            // GitHub API 要求必须带 User-Agent
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TaskSchedulerApp", CurrentVersion));
            client.Timeout = TimeSpan.FromSeconds(15);

            string apiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

            onProgress?.Invoke("获取版本信息...");
            string json = await client.GetStringAsync(apiUrl);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var release = JsonSerializer.Deserialize<GitHubRelease>(json, options);

            if (release == null || string.IsNullOrEmpty(release.tag_name))
            {
                MessageBox.Info("未能获取到版本信息，请稍后再试。", "检查更新");
                return;
            }

            string remoteVersionStr = release.tag_name.Replace("v", "").Replace("V", "");
            Version localVer = new Version(CurrentVersion);
            Version remoteVer;

            if (!Version.TryParse(remoteVersionStr, out remoteVer))
            {
                MessageBox.Error("解析远程版本号失败。", "错误");
                return;
            }

            if (remoteVer > localVer)
            {
                // 寻找 .exe 下载链接
                string? downloadUrl = null;
                if (release.assets != null)
                {
                    foreach (var asset in release.assets)
                    {
                        if (asset.browser_download_url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.browser_download_url;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    MessageBox.Warning("发现新版本，但未找到可下载的安装包(.exe)。", "更新提示");
                    return;
                }

                var result = MessageBox.Show($"发现新版本: v{remoteVersionStr}\n更新内容:\n{release.body}\n\n是否立即下载并更新？",
                                             "发现新版本", MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    await DownloadAndInstallUpdate(downloadUrl, client, onProgress);
                }
            }
            else
            {
                MessageBox.Success("当前已经是最新版本！", "检查更新");
            }
        }
        catch (HttpRequestException)
        {
            MessageBox.Error("无法连接到 GitHub，请检查网络或是否开启了代理。", "网络错误");
        }
    }

    private static async Task DownloadAndInstallUpdate(string downloadUrl, HttpClient client, Action<string> onProgress)
    {
        try
        {
            onProgress?.Invoke("正在下载新版本...");

            // 下载到系统的临时文件夹
            string tempFilePath = Path.Combine(Path.GetTempPath(), $"TaskSchedulerApp_Update_v{DateTime.Now.Ticks}.exe");

            using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                using (var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs);
                }
            }

            onProgress?.Invoke("下载完成，准备安装...");

            // 启动安装程序 (传入 /SILENT 实现静默安装覆盖，具体取决于你打包工具的参数)
            Process.Start(new ProcessStartInfo
            {
                FileName = tempFilePath,
                Arguments = "/SILENT", // Inno Setup 的静默安装参数
                UseShellExecute = true
            });

            // 关闭当前应用，腾出文件占用让安装包覆盖
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Error("下载或安装更新时发生错误:\n" + ex.Message, "更新失败");
        }
    }
}

// 对应 GitHub API 的 JSON 结构
public class GitHubRelease
{
    public string? tag_name { get; set; }
    public string? body { get; set; }
    public GitHubAsset[]? assets { get; set; }
}

public class GitHubAsset
{
    public string? browser_download_url { get; set; }
}