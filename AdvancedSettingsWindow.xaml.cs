using System.Net.Http;
using System.Windows;
using TaskSchedulerApp.Models;

namespace TaskSchedulerApp
{
    public partial class AdvancedSettingsWindow : HandyControl.Controls.Window
    {
        private AppSettings _settings;

        public AdvancedSettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            DataContext = _settings;
        }

        private async void TestPush_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_settings.BarkUrl))
            {
                HandyControl.Controls.MessageBox.Warning("请先填写 Bark Server URL");
                return;
            }
            await Task.Run(async () =>
            {
                try
                {
                    // 静态调用，避免实例化 AutomationService
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    string barkUrl = _settings.BarkUrl.TrimEnd('/');
                    string url = $"{barkUrl}/{Uri.EscapeDataString("测试推送")}/{Uri.EscapeDataString("配置正确！")}";
                    if (!string.IsNullOrWhiteSpace(_settings.BarkIcon))
                        url += $"?icon={Uri.EscapeDataString(_settings.BarkIcon)}";
                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    Dispatcher.Invoke(() => HandyControl.Controls.MessageBox.Success("Ciallo⁓\n 推送成功！✅"));
                }
                catch
                {
                    Dispatcher.Invoke(() => HandyControl.Controls.MessageBox.Error("推送失败"));
                }
            });
        }

        private void SelectScreenshotFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _settings.ScreenshotPath = dlg.SelectedPath;
            }
        }

        private void SelectLogFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _settings.LogPath = dlg.SelectedPath;
            }
        }

        private void SaveAndClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}