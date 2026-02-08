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
            HandyControl.Controls.MessageBox.Info("请在任务运行时测试，或检查 Bark URL 是否正确。");
        }

        private void SelectScreenshotFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _settings.ScreenshotPath = dlg.SelectedPath;
            }
        }

        // 选择日志路径
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