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
        private readonly object _logLock = new object();

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

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await UpdateManager.CheckOnStartup();
        }

        public void HideWindow()
        {
            this.Hide();
            if (_miniLogWindow != null) { _miniLogWindow.Close(); _miniLogWindow = null; }
            try
            {
                _miniLogWindow = new MiniLogWindow(_viewModel.Logs, _viewModel.Settings, () => { RestoreMainWindow(); });
                _miniLogWindow.Show();
            }
            catch (Exception ex) { _viewModel.Log("错误", "MiniLog Error: " + ex.Message); this.Show(); }
        }

        public void RestoreMainWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            if (_miniLogWindow != null) { try { _miniLogWindow.Close(); } catch { } _miniLogWindow = null; }
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
            catch { try { _notifyIcon.Icon =SystemIcons.Application; } catch { } }

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
            if ((DateTime.Now - _lastHotkeyTime).TotalMilliseconds < 500) return;

            if (NativeMethods.GetAsyncKeyState(0x78) != 0 && _viewModel.SelectedTask != null) // F9
            {
                _lastHotkeyTime = DateTime.Now;
                var point = WinForms.Cursor.Position;
                _viewModel.SelectedTask.PosX = point.X;
                _viewModel.SelectedTask.PosY = point.Y;
                _viewModel.Log("操作", $"已录制坐标: {point.X}, {point.Y}");
                _viewModel.SaveConfigCommand.Execute(null);
            }

            if (NativeMethods.GetAsyncKeyState(0x77) != 0 && _viewModel.SelectedTask != null) // F8
            {
                _lastHotkeyTime = DateTime.Now;
                var point = WinForms.Cursor.Position;
                string title = NativeMethods.GetWindowTitleFromPoint(point.X, point.Y);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    _viewModel.SelectedTask.WindowTitle = title;
                    _viewModel.Log("操作", $"F8 抓取: [{title}]");
                    _viewModel.SaveConfigCommand.Execute(null);
                }
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
        public ICommand DebugRunCommand { get; }
        public ICommand OpenProgramCommand { get; }
        public ICommand OpenAboutCommand { get; }

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
            // 新增：浏览额外文件
            BrowseExtraFileCommand = new RelayCommand(BrowseExtraFile);

            StartTaskCommand = new RelayCommand(StartTasks);
            StopTaskCommand = new RelayCommand(() => { try { _runner?.RequestStop(); } catch (Exception ex) { Log("错误", ex.Message); } });
            TestClickCommand = new RelayCommand(() => { if (SelectedTask != null) NativeMethods.ClickLeft(SelectedTask.PosX, SelectedTask.PosY); });
            ShowImageLogCommand = new RelayCommand(() => new ImageLogWindow(Settings.ScreenshotPath).Show());
            DebugRunCommand = new RelayCommand(DebugRun);
            OpenProgramCommand = new RelayCommand(OpenProgram);
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

        private void OpenProgram()
        {
            if (SelectedTask == null || !File.Exists(SelectedTask.Path)) { HandyControl.Controls.MessageBox.Warning("路径无效"); return; }
            try { Process.Start(new ProcessStartInfo { FileName = SelectedTask.Path, UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(SelectedTask.Path), Arguments = "" }); }
            catch (Exception ex) { Log("错误", ex.Message); }
        }

        private async void DebugRun()
        {
            if (SelectedTask == null || !File.Exists(SelectedTask.Path)) return;
            if (IsRunning) { Log("警告", "已有任务运行中"); return; }

            var dlg = new DebugRunWindow(SelectedTask);
            if (dlg.ShowDialog() != true) return;

            IsRunning = true;
            Logs.Clear();

            try
            {
                if (dlg.SmartHide) _view.HideWindow();
                _runner = new AutomationService(Settings, Log);

                bool originalZombie = SelectedTask.IsZombieCheckEnabled;
                string originalArgs = SelectedTask.Arguments;

                SelectedTask.IsZombieCheckEnabled = dlg.EnableZombieCheck;
                if (!dlg.UseArgs) SelectedTask.Arguments = "";

                Log("调试", $"=== 调试: {SelectedTask.Name} ===");
                await _runner.RunSingleTask(SelectedTask);

                SelectedTask.IsZombieCheckEnabled = originalZombie;
                SelectedTask.Arguments = originalArgs;
            }
            catch (Exception ex) { Log("错误", "调试异常: " + ex.Message); }
            finally
            {
                IsRunning = false;
                _runner = null;
                Application.Current.Dispatcher.Invoke(() => _view.RestoreMainWindow());
                Log("调试", "=== 结束 ===");
            }
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