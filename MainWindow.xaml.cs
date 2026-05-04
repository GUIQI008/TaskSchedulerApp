using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
        private MacroRecorder? _recorder;

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
        private void ActionsDataGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Delete && _viewModel.SelectedTask?.Actions != null)
            {
                if ((sender as DataGrid)?.SelectedItem is MacroAction selectedAction)
                {
                    _viewModel.SelectedTask.Actions.Remove(selectedAction);
                    e.Handled = true;
                }
            }
        }
        public void HideWindow()
        {
            this.Hide();

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
            // 停止录制并保存
            StopRecording();
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
            if (!this.IsActive) return;
            if ((DateTime.Now - _lastHotkeyTime).TotalMilliseconds < 500) return;
            if (_viewModel.SelectedTask == null) return;

            // F9 - 录制左键
            if (NativeMethods.GetAsyncKeyState(0x78) != 0)
            {
                _lastHotkeyTime = DateTime.Now;
                RecordClick("Left");
            }
            // Shift+F9 录制右键
            else if (NativeMethods.GetAsyncKeyState(0x78) != 0 && (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0)
            {
                _lastHotkeyTime = DateTime.Now;
                RecordClick("Right");
            }
            // F8 - 智能填入窗口标题
            else if (NativeMethods.GetAsyncKeyState(0x77) != 0)
            {
                _lastHotkeyTime = DateTime.Now;
                SmartFillWindowTitle();
            }
        }

        /// <summary>
        /// 根据鼠标当前悬停的 TextBox 智能填入窗口标题
        /// </summary>
        private void SmartFillWindowTitle()
        {
            var task = _viewModel.SelectedTask;
            if (task == null) return;

            // 获取鼠标当前悬停的 WPF 元素
            var element = Mouse.DirectlyOver as DependencyObject;
            if (element == null)
            {
                _viewModel.Log("警告", "F8：无法检测到目标输入框，请将鼠标移至输入框上方再按 F8。");
                return;
            }

            // 沿视觉树查找目标 TextBox
            bool isGameTitle = FindParentByName(element, "GameTitleBox") != null;
            bool isMacroTitle = FindParentByName(element, "MacroTitleBox") != null;

            if (!isGameTitle && !isMacroTitle)
            {
                _viewModel.Log("提示", "F8：请将鼠标悬停在“游戏窗口标题”或“脚本窗口标题”输入框上再按。");
                return;
            }

            // 抓取鼠标屏幕坐标的窗口标题
            var screenPoint = WinForms.Cursor.Position;
            string title = NativeMethods.GetWindowTitleFromPoint(screenPoint.X, screenPoint.Y);
            if (string.IsNullOrWhiteSpace(title))
            {
                _viewModel.Log("警告", "F8：该位置未找到窗口标题。");
                return;
            }

            if (isGameTitle)
            {
                task.WindowTitle = title;
                _viewModel.Log("操作", $"F8 抓取游戏窗口标题: [{title}]");
            }
            else
            {
                task.MacroWindowTitle = title;
                _viewModel.Log("操作", $"F8 抓取脚本窗口标题: [{title}]");
            }
            // 仅修改内存，不立即保存（保存由用户手动或关闭时触发）
        }

        /// <summary>
        /// 在父链中查找指定名称的控件
        /// </summary>
        private FrameworkElement? FindParentByName(DependencyObject child, string name)
        {
            while (child != null)
            {
                if (child is FrameworkElement fe && fe.Name == name)
                    return fe;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }
        private void RecordClick(string button)
        {
            var task = _viewModel.SelectedTask;
            if (task == null) return;

            try
            {
                // 确保录制器已启动
                if (_recorder == null || _recorder.Status != RecordStatus.Recording)
                {
                    _recorder = new MacroRecorder(task);
                    _recorder.Start();
                    _viewModel.IsRecording = true;
                    _viewModel.Log("录制", "开始录制宏，请按 F9 (左键) 或 Shift+F9 (右键) 在脚本窗口上点击。");
                }

                var screenPoint = WinForms.Cursor.Position;
                bool success = _recorder.RecordClick(button, screenPoint);
                if (success)
                {
                    _viewModel.Log("操作", $"{button}键点击已录制，客户区坐标已保存。");
                }
                else
                {
                    _viewModel.Log("警告", "录制失败：鼠标不在目标脚本窗口上，或脚本窗口未匹配。");
                }
            }
            catch (Exception ex)
            {
                _viewModel.Log("错误", $"录制异常：{ex.Message}");
            }
        }

        public void StopRecording()
        {
            if (_recorder != null && _recorder.Status == RecordStatus.Recording)
            {
                _recorder.Stop();
                _viewModel.IsRecording = false;
                _viewModel.Log("录制", "停止录制。");
                // 停止录制时自动保存一次配置
                _viewModel.SaveConfigCommand.Execute(null);
            }
            _recorder = null;
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
        private bool _isRecording;
        private AutomationService? _runner;
        private readonly object _logLock = new object();

        public AppSettings Settings { get => _settings; set { _settings = value; OnPropertyChanged(); } }
        public TaskItem? SelectedTask { get => _selectedTask; set { _selectedTask = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsTaskSelected)); } }
        public ObservableCollection<LogEntry> Logs { get; set; } = new ObservableCollection<LogEntry>();
        public bool IsRunning { get => _isRunning; set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotRunning)); } }
        public bool IsNotRunning => !IsRunning;
        public bool IsTaskSelected => SelectedTask != null;
        public bool IsRecording { get => _isRecording; set { _isRecording = value; OnPropertyChanged(); } }

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
        public ICommand StopRecordingCommand { get; }

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
            TestClickCommand = new RelayCommand(async () => {
                if (SelectedTask != null && SelectedTask.Actions != null)
                {
                    Log("测试", "开始回放测试...");
                    // 这里调用自动化服务的单任务测试，可直接使用临时服务或模拟
                    var testService = new AutomationService(Settings, Log);
                    await testService.RunSingleTask(SelectedTask);
                    Log("测试", "回放测试结束。");
                }
            });
            ShowImageLogCommand = new RelayCommand(() => new ImageLogWindow(Settings.ScreenshotPath).Show());
            StopRecordingCommand = new RelayCommand(() => _view.StopRecording());
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