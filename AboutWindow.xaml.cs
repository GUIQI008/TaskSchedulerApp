using System;
using System.Windows;

namespace TaskSchedulerApp.Views
{
    public partial class AboutWindow : HandyControl.Controls.Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            VersionText.Text = "当前版本: v" + UpdateManager.CurrentVersion;
        }

        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdate.IsEnabled = false;
            BtnCheckUpdate.Content = "正在连接 GitHub...";

            try
            {
                await UpdateManager.CheckForUpdatesManual(
                    onProgress: (msg) => {
                        // 回到UI线程更新按钮文本
                        Dispatcher.Invoke(() => BtnCheckUpdate.Content = msg);
                    });
            }
            catch (Exception ex)
            {
                HandyControl.Controls.MessageBox.Error("更新失败: " + ex.Message, "错误");
            }
            finally
            {
                BtnCheckUpdate.IsEnabled = true;
                BtnCheckUpdate.Content = "检查更新";
            }
        }
    }
}