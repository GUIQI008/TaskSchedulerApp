using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using TaskSchedulerApp.Models;

namespace TaskSchedulerApp
{
    public partial class MiniLogWindow : Window
    {
        private Action _restoreAction;
        private AppSettings _settings;

        public MiniLogWindow(ObservableCollection<LogEntry> logs, AppSettings settings, Action restoreAction)
        {
            InitializeComponent();
            _restoreAction = restoreAction;
            _settings = settings;

            MiniLogList.ItemsSource = logs;

            // 自动滚动
            logs.CollectionChanged += (s, e) => {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    Dispatcher.Invoke(() => {
                        if (MiniLogList.Items.Count > 0)
                            MiniLogList.ScrollIntoView(MiniLogList.Items[MiniLogList.Items.Count - 1]);
                    });
                }
            };

            //初始化时恢复大小和位置
            this.Loaded += (s, e) => {
                if (_settings.MiniLogWidth > 100) this.Width = _settings.MiniLogWidth;
                if (_settings.MiniLogHeight > 100) this.Height = _settings.MiniLogHeight;
                if (_settings.MiniLogLeft >= 0 && _settings.MiniLogTop >= 0)
                {
                    if (_settings.MiniLogLeft < SystemParameters.VirtualScreenWidth &&
                        _settings.MiniLogTop < SystemParameters.VirtualScreenHeight)
                    {
                        this.Left = _settings.MiniLogLeft;
                        this.Top = _settings.MiniLogTop;
                    }
                }
                else
                {
                    // 默认位置
                    var workingArea = SystemParameters.WorkArea;
                    this.Left = workingArea.Right - this.Width - 20;
                    this.Top = workingArea.Bottom - this.Height - 20;
                }
            };

            //关闭前保存状态
            this.Closing += (s, e) => {
                _settings.MiniLogWidth = this.Width;
                _settings.MiniLogHeight = this.Height;
                _settings.MiniLogLeft = this.Left;
                _settings.MiniLogTop = this.Top;
            };
        }

        private void DragWindow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void Restore_MouseDoubleClick(object sender, MouseButtonEventArgs e) => TriggerRestore();
        private void RestoreButton_Click(object sender, RoutedEventArgs e) => TriggerRestore();

        private void TriggerRestore()
        {
            _restoreAction?.Invoke();
            this.Close();
        }
    }
}