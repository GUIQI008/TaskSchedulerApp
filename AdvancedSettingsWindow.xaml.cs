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

        private void TestPush_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_settings.BarkUrl))
            {
                HandyControl.Controls.MessageBox.Warning("请先填写 Bark Server URL");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    var service = new TaskSchedulerApp.Services.AutomationService(_settings, (t, m) => { });
                    await service.SendBark("GameSchedule 测试推送", "Ciallo⁓\nBark 配置正确！✅");

                    Dispatcher.Invoke(() =>
                        HandyControl.Controls.MessageBox.Success("测试推送已发送！请检查手机。"));
                }
                catch
                {
                    Dispatcher.Invoke(() =>
                        HandyControl.Controls.MessageBox.Error("推送失败，请检查网络或Bark URL"));
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