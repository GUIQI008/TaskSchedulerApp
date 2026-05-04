using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using TaskSchedulerApp.Models;

namespace TaskSchedulerApp
{
    public partial class MiniLogWindow : Window
    {
        private readonly Action _restoreAction;
        private readonly AppSettings _settings;
        private bool _isLoaded = false;
        private DateTime _lastScrollTime = DateTime.MinValue;

        public MiniLogWindow(ObservableCollection<LogEntry> logs, AppSettings settings, Action restoreAction)
        {
            InitializeComponent();
            _restoreAction = restoreAction;
            _settings = settings;

            MiniLogList.ItemsSource = logs;

            logs.CollectionChanged += (s, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    if ((DateTime.Now - _lastScrollTime).TotalMilliseconds > 200)
                    {
                        _lastScrollTime = DateTime.Now;
                        Dispatcher.Invoke(() =>
                        {
                            if (MiniLogList.Items.Count > 0)
                                MiniLogList.ScrollIntoView(MiniLogList.Items[MiniLogList.Items.Count - 1]);
                        });
                    }
                }
            };

            this.LocationChanged += (s, e) => SaveWindowState();
            this.SizeChanged += (s, e) => SaveWindowState();

            this.Loaded += MiniLogWindow_Loaded;
            this.Closing += MiniLogWindow_Closing;
        }

        private void MiniLogWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_settings.MiniLogWidth > 50)
            {
                this.Width = _settings.MiniLogWidth;
                this.Height = _settings.MiniLogHeight;
                this.Left = _settings.MiniLogLeft;
                this.Top = _settings.MiniLogTop;
                
                SnapToEdge();
            }
            else
            {
                var area = SystemParameters.WorkArea;
                this.Left = area.Right - this.Width - 20;
                this.Top = area.Top + 40;
            }

            _isLoaded = true;
        }

        private void DragWindow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) 
            {
                this.DragMove();
                SnapToEdge();
            }
        }

        private void SnapToEdge()
        {
            double snapDist = 25; // 靠近屏幕多少像素时触发吸附

            // 【关键修改】：这里填入你 XAML 中 Border 的 Margin 值 (目前是 10)
            // 如果你以后把 Margin 改成了 15，这里也要改成 15
            double shadowMargin = 10;

            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            var screen = System.Windows.Forms.Screen.FromHandle(helper.Handle).WorkingArea;

            // 贴左边：允许窗口物理左侧溢出屏幕外 shadowMargin 个像素
            if (this.Left < screen.Left + snapDist)
                this.Left = screen.Left - shadowMargin;

            // 贴上边
            if (this.Top < screen.Top + snapDist)
                this.Top = screen.Top - shadowMargin;

            // 贴右边：右侧物理边界溢出屏幕外
            if (this.Left + this.Width > screen.Right - snapDist)
                this.Left = screen.Right - this.Width + shadowMargin;

            // 贴下边
            if (this.Top + this.Height > screen.Bottom - snapDist)
                this.Top = screen.Bottom - this.Height + shadowMargin;

            SaveWindowState();
        }

        private void SaveWindowState()
        {
            if (!_isLoaded || this.WindowState == WindowState.Minimized) return;

            _settings.MiniLogWidth = this.ActualWidth;
            _settings.MiniLogHeight = this.ActualHeight;
            _settings.MiniLogLeft = this.Left;
            _settings.MiniLogTop = this.Top;
        }

        private void MiniLogWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveWindowState();
        }

        private void Restore_MouseDoubleClick(object sender, MouseButtonEventArgs e) => TriggerRestore();
        private void RestoreButton_Click(object sender, RoutedEventArgs e) => TriggerRestore();
        private void TriggerRestore() { _restoreAction?.Invoke(); this.Close(); }
    }
}