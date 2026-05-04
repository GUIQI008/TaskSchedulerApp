using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using TaskSchedulerApp.Core;
using TaskSchedulerApp.Models;
using TaskSchedulerApp.Services;
using Application = System.Windows.Application;
using WinForms = System.Windows.Forms;

namespace TaskSchedulerApp
{
    public partial class MainWindow : HandyControl.Controls.Window
    {
        private MainViewModel _viewModel = null!;
        private NotifyIcon? _notifyIcon;
        private DispatcherTimer _hotkeyTimer;
        private MiniLogWindow? _miniLogWindow;
        private DateTime _lastHotkeyTime = DateTime.MinValue;
        private bool _isContinuousRecording = false;
        private DateTime _lastClickTime = DateTime.Now;
        private bool _wasF9Pressed = false;
        private bool _wasLeftMouseDown = false;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel(this);
            DataContext = _viewModel;
            this.Loaded += MainWindow_Loaded;

            _viewModel.Logs.CollectionChanged += (s, e) => {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (LogListBox != null && LogListBox.Items.Count > 0)
                        {
                            try { LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]); } catch { }
                        }
                    });
                }
            };

            InitIcons();

            _hotkeyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _hotkeyTimer.Tick += HotkeyTimer_Tick;
            _hotkeyTimer.Start();

            CleanOldScreenshots();
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {

        }

        public void HideWindow()
        {
            this.Hide();

            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    NativeMethods.SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
                }
            }
            catch { }

            if (_miniLogWindow != null)
            {
                try { _miniLogWindow.Close(); } catch { }
                _miniLogWindow = null;
            }

            try
            {
                _miniLogWindow = new MiniLogWindow(_viewModel.Logs, _viewModel.Settings, () => { RestoreMainWindow(); });
                _miniLogWindow.Show();
            }
            catch (Exception ex)
            {
                _viewModel.Log("错误", "MiniLog 创建失败: " + ex.Message);

                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            }
        }

        public void RestoreMainWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();

            if (_miniLogWindow != null)
            {
                try { _miniLogWindow.Close(); } catch { }
                _miniLogWindow = null;
            }

            _viewModel.SaveConfigCommand.Execute(null);
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_viewModel.IsRunning) { e.Cancel = true; HideWindow(); return; }
            _viewModel.SaveConfigCommand.Execute(null);
            if (_notifyIcon != null) { _notifyIcon.Visible = false; _notifyIcon.Dispose(); }
            if (_miniLogWindow != null) _miniLogWindow.Close();
            Application.Current.Shutdown();
        }

        private void InitIcons()
        {
            _notifyIcon = new WinForms.NotifyIcon();
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (File.Exists(exePath))
                {
                    var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (icon != null)
                    {
                        _notifyIcon.Icon = icon;
                        this.Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    }
                }
            }
            catch { try { _notifyIcon.Icon = SystemIcons.Application; } catch { } }

            _notifyIcon.Visible = true;
            _notifyIcon.Text = "自动化任务工具";
            var contextMenu = new ContextMenuStrip();

            var stopItem = new ToolStripMenuItem("停止当前任务");
            stopItem.Click += (s, e) => { if (_viewModel.IsRunning) _viewModel.StopTaskCommand.Execute(null); };

            var showItem = new ToolStripMenuItem("显示主窗口");
            showItem.Click += (s, e) => RestoreMainWindow();

            var exitItem = new ToolStripMenuItem("完全退出");
            exitItem.Click += (s, e) => {
                if (_viewModel.IsRunning) _viewModel.StopTaskCommand.Execute(null);
                _viewModel.SaveConfigCommand.Execute(null);
                _notifyIcon.Visible = false; _notifyIcon.Dispose();
                Application.Current.Shutdown();
            };

            contextMenu.Items.Add(showItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(stopItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);
            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => RestoreMainWindow();
        }

        private void HotkeyTimer_Tick(object? sender, EventArgs e)
        {
            if (!this.IsActive && !_isContinuousRecording) return;

            bool isCtrlPressed = (NativeMethods.GetAsyncKeyState(0x11) & 0x8000) != 0;
            bool isF9Pressed = (NativeMethods.GetAsyncKeyState(0x78) & 0x8000) != 0;
            bool isLeftMouseDown = (NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0;

            // 1. 处理 Ctrl+F9 开关连点录制
            if (isCtrlPressed && isF9Pressed && !_wasF9Pressed && _viewModel.SelectedTask != null)
            {
                _isContinuousRecording = !_isContinuousRecording;
                _lastClickTime = DateTime.Now;
                _viewModel.Log("操作", _isContinuousRecording ? "🔴 已开启连续录制，尽情点击吧！" : "⏹ 连续录制结束！");
                if (!_isContinuousRecording) _viewModel.SaveConfigCommand.Execute(null);
            }
            // 2. 处理单点 F9 录制
            else if (!isCtrlPressed && isF9Pressed && !_wasF9Pressed && _viewModel.SelectedTask != null)
            {
                var pt = WinForms.Cursor.Position;
                _viewModel.SelectedTask.Actions.Add(new MacroAction { ActionType = MacroActionType.MouseLeftClick, X = pt.X, Y = pt.Y, DelayBefore = 1000, Description = "F9单击录制" });
                _viewModel.Log("操作", $"录制单击 X:{pt.X}, Y:{pt.Y}");
                _viewModel.SaveConfigCommand.Execute(null);
            }
            _wasF9Pressed = isF9Pressed;

            // 3. 处理连续录制时的左键点击捕获
            if (_isContinuousRecording && _viewModel.SelectedTask != null)
            {
                if (isLeftMouseDown && !_wasLeftMouseDown) // 鼠标刚刚按下
                {
                    int delay = (int)(DateTime.Now - _lastClickTime).TotalMilliseconds;
                    if (delay > 5000) delay = 1000; // 防呆
                    if (delay < 50) delay = 50;

                    var pt = WinForms.Cursor.Position;
                    _viewModel.SelectedTask.Actions.Add(new MacroAction { ActionType = MacroActionType.MouseLeftClick, X = pt.X, Y = pt.Y, DelayBefore = delay, Description = "连续录制" });
                    _viewModel.Log("操作", $"捕获点击 X:{pt.X}, Y:{pt.Y}");
                    _lastClickTime = DateTime.Now;
                }
                _wasLeftMouseDown = isLeftMouseDown;
            }
        }

        private void CleanOldScreenshots()
        {
            Task.Run(() => { try { if (Directory.Exists(_viewModel.Settings.ScreenshotPath)) foreach (var f in Directory.GetFiles(_viewModel.Settings.ScreenshotPath)) if (new FileInfo(f).CreationTime < DateTime.Now.AddHours(-24)) File.Delete(f); } catch { } });
        }
    }

    public class MainViewModel : ObservableObject
    {
        private MainWindow _view;
        private AppSettings _settings;
        private TaskItem? _selectedTask;
        private bool _isRunning;
        private AutomationService? _runner;
        private readonly object _logLock = new object();

        public AppSettings Settings { get => _settings; set { _settings = value; OnPropertyChanged(); } }
        public TaskItem? SelectedTask { get => _selectedTask; set { _selectedTask = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsTaskSelected)); } }
        public ObservableCollection<LogEntry> Logs { get; set; } = new ObservableCollection<LogEntry>();
        public bool IsRunning { get => _isRunning; set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotRunning)); } }
        public bool IsNotRunning => !IsRunning;
        public bool IsTaskSelected => SelectedTask != null;

        public ICommand SaveConfigCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand AddTaskCommand { get; }
        public ICommand RemoveTaskCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand BrowseFileCommand { get; }
        public ICommand BrowseExtraFileCommand { get; }
        public ICommand StartTaskCommand { get; }
        public ICommand StopTaskCommand { get; }
        public ICommand TestClickCommand { get; }
        public ICommand ShowImageLogCommand { get; }
        public ICommand OpenAboutCommand { get; }
        public ICommand ClearMacrosCommand { get; }

        public MainViewModel(MainWindow view)
        {
            _view = view;
            _settings = new AppSettings();
            LoadConfig();

            SaveConfigCommand = new RelayCommand(SaveConfig);
            OpenSettingsCommand = new RelayCommand(() => new AdvancedSettingsWindow(Settings).ShowDialog());
            OpenAboutCommand = new RelayCommand(OpenAbout);

            AddTaskCommand = new RelayCommand(() => { Settings.TaskList.Add(new TaskItem { Name = "新任务" }); SaveConfig(); });
            RemoveTaskCommand = new RelayCommand(() => { if (SelectedTask != null) { Settings.TaskList.Remove(SelectedTask); SaveConfig(); } });
            MoveUpCommand = new RelayCommand(() => { if (SelectedTask != null && Settings.TaskList.IndexOf(SelectedTask) > 0) { Settings.TaskList.Move(Settings.TaskList.IndexOf(SelectedTask), Settings.TaskList.IndexOf(SelectedTask) - 1); SaveConfig(); } });
            MoveDownCommand = new RelayCommand(() => { if (SelectedTask != null && Settings.TaskList.IndexOf(SelectedTask) < Settings.TaskList.Count - 1) { Settings.TaskList.Move(Settings.TaskList.IndexOf(SelectedTask), Settings.TaskList.IndexOf(SelectedTask) + 1); SaveConfig(); } });

            BrowseFileCommand = new RelayCommand(BrowseFile);
            BrowseExtraFileCommand = new RelayCommand(BrowseExtraFile);

            StartTaskCommand = new RelayCommand(StartTasks);
            StopTaskCommand = new RelayCommand(() => { try { _runner?.RequestStop(); } catch (Exception ex) { Log("错误", ex.Message); } });
            ShowImageLogCommand = new RelayCommand(() => new ImageLogWindow(Settings.ScreenshotPath).Show());
            ClearMacrosCommand = new RelayCommand(() => {
                if (SelectedTask != null)
                {
                    SelectedTask.Actions.Clear();
                    SaveConfigCommand.Execute(null);
                    Log("操作", "已清空宏动作列表");
                }
            });

            // 替换测试点击的绑定 (加入防遮挡和遍历)
            TestClickCommand = new RelayCommand(async () => {
                if (SelectedTask != null && SelectedTask.Actions != null && SelectedTask.Actions.Count > 0)
                {
                    Log("测试", "准备回放，防遮挡检测中...");
                    bool foundScript = false;
                    if (!string.IsNullOrWhiteSpace(SelectedTask.ScriptWindowTitle))
                    {
                        IntPtr hwnd = NativeMethods.FindWindow(null, SelectedTask.ScriptWindowTitle);
                        if (hwnd != IntPtr.Zero)
                        {
                            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                            NativeMethods.SetForegroundWindow(hwnd);
                            foundScript = true;
                        }
                    }

                    if (!foundScript) Application.Current.Dispatcher.Invoke(() => _view.WindowState = WindowState.Minimized);
                    await Task.Delay(500);

                    foreach (var action in SelectedTask.Actions)
                    {
                        if (action.DelayBefore > 0) await Task.Delay(action.DelayBefore);
                        InputSimulator.ExecuteAction(action);
                    }

                    Log("测试", "回放测试结束");
                    Application.Current.Dispatcher.Invoke(() => { _view.WindowState = WindowState.Normal; _view.Activate(); });
                }
            });

        }

        private void LoadConfig()
        {
            if (File.Exists("config.json")) try { var l = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText("config.json")); if (l != null) Settings = l; } catch { }
        }

        private void OpenAbout()
        {
            var aboutWin = new TaskSchedulerApp.Views.AboutWindow { Owner = Application.Current.MainWindow };
            aboutWin.ShowDialog();
        }

        private void SaveConfig()
        {
            try
            {
                string json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
                File.WriteAllText("config.json", json);
                Log("系统", "配置已保存");
            }
            catch (Exception ex)
            {
                Log("错误", "保存失败: " + ex.Message);
            }
        }

        private void BrowseFile()
        {
            if (SelectedTask == null) return;
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Executables|*.exe;*.bat;*.cmd|All Files|*.*" };
            if (dlg.ShowDialog() == true) SelectedTask.Path = dlg.FileName;
        }

        private void BrowseExtraFile()
        {
            if (SelectedTask == null) return;
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Executables|*.exe;*.bat;*.cmd|All Files|*.*" };
            if (dlg.ShowDialog() == true) SelectedTask.ExtraStartPath = dlg.FileName;
        }

        

        public async void StartTasks()
        {
            if (Settings.TaskList.Count == 0) return;
            IsRunning = true;
            _view.HideWindow();
            _runner = new AutomationService(Settings, Log);

            try
            {
                await Task.Run(async () => await _runner.RunAllTasks(SelectedTask));
            }
            catch (Exception ex) { Log("错误", ex.Message); }
            finally
            {
                IsRunning = false;
                Application.Current.Dispatcher.Invoke(() => _view.RestoreMainWindow());
            }
        }

        public void Log(string type, string msg)
        {
            if (Application.Current == null) return;
            Application.Current.Dispatcher.InvokeAsync(() => {
                lock (_logLock)
                {
                    string c = type switch { "错误" => "#FF5555", "监控" => "#8BE9FD", "系统" => "#50FA7B", _ => "#F8F8F2" };
                    Logs.Add(new LogEntry { Time = DateTime.Now.ToString("HH:mm:ss"), Message = $"[{type}] {msg}", Color = c });
                    if (Logs.Count > 200) Logs.RemoveAt(0);
                }
            });
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _e;
        public RelayCommand(Action e) => _e = e;
        public event EventHandler? CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => _e();
    }
}