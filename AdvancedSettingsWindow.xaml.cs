using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TaskSchedulerApp.Models;

namespace TaskSchedulerApp
{
    public partial class AdvancedSettingsWindow : Window
    {
        private AppSettings _settings;

        public AdvancedSettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            DataContext = _settings;
        }

        private void DragWindow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
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
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                    string barkUrl = _settings.BarkUrl.TrimEnd('/');
                    string url = $"{barkUrl}/{Uri.EscapeDataString("测试推送")}/{Uri.EscapeDataString("Ciallo⁓\n 推送成功！✅")}";
                    if (!string.IsNullOrWhiteSpace(_settings.BarkIcon))
                        url += $"?icon={Uri.EscapeDataString(_settings.BarkIcon)}";

                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    Dispatcher.Invoke(() => HandyControl.Controls.MessageBox.Success("推送成功！"));
                }
                catch
                {
                    Dispatcher.Invoke(() => HandyControl.Controls.MessageBox.Error("推送失败，请检查 URL 或网络"));
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