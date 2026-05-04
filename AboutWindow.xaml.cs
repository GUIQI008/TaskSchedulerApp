using System;
using System.Windows;
using System.Windows.Input;

namespace TaskSchedulerApp.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            VersionText.Text = "当前版本: v" + UpdateManager.CurrentVersion;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdate.IsEnabled = false;
            BtnCheckUpdate.Content = "正在连接 GitHub...";

            try
            {
                await UpdateManager.CheckForUpdatesManual(
                    onProgress: (msg) =>
                    {
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