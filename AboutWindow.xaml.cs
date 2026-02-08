using System.Windows;

namespace TaskSchedulerApp.Views
{
    public partial class AboutWindow : HandyControl.Controls.Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            // 读取 UpdateManager 中的版本号
            VersionText.Text = "v" + UpdateManager.CurrentVersion;
        }
    }
}