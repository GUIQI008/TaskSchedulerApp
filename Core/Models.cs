#nullable enable
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TaskSchedulerApp.Models
{
    public class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public enum MacroActionType
    {
        Delay, MouseMove, MouseLeftDown, MouseLeftUp, MouseRightDown, MouseRightUp, KeyDown, KeyUp
    }

    public class MacroAction : ObservableObject
    {
        private MacroActionType _actionType;
        private int _delayBefore;
        private int _x;
        private int _y;
        private int _keyCode;
        private string _description = "";

        public MacroActionType ActionType { get => _actionType; set { _actionType = value; OnPropertyChanged(); } }
        public int DelayBefore { get => _delayBefore; set { _delayBefore = value; OnPropertyChanged(); } }
        public int X { get => _x; set { _x = value; OnPropertyChanged(); } }
        public int Y { get => _y; set { _y = value; OnPropertyChanged(); } }
        public int KeyCode { get => _keyCode; set { _keyCode = value; OnPropertyChanged(); } }
        public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
    }

    public class TaskItem : ObservableObject
    {
        private string name = "新任务";
        private string path = "";
        private string arguments = "";
        private string windowTitle = "";
        private int recognitionTimeout = 15;
        private string processNames = "";
        private string extraProcessNames = "";
        private int runTime = 60;
        private string status = "等待中";
        private string extraStartPath = "";
        private string extraStartArguments = "";
        private string macroWindowTitle = "";

        // 【新增】录制的动作集合
        private ObservableCollection<MacroAction> actions = new ObservableCollection<MacroAction>();

        public string Name { get => name; set { name = value; OnPropertyChanged(); } }
        public string Path { get => path; set { path = value; OnPropertyChanged(); } }
        public string Arguments { get => arguments; set { arguments = value; OnPropertyChanged(); } }
        public string WindowTitle { get => windowTitle; set { windowTitle = value; OnPropertyChanged(); } }

        public int RecognitionTimeout { get => recognitionTimeout; set { recognitionTimeout = value; OnPropertyChanged(); } }
        public string ProcessNames { get => processNames; set { processNames = value; OnPropertyChanged(); } }

        public string ExtraProcessNames { get => extraProcessNames; set { extraProcessNames = value; OnPropertyChanged(); } }
        public int RunTime { get => runTime; set { runTime = value; OnPropertyChanged(); } }
        public string Status { get => status; set { status = value; OnPropertyChanged(); } }

        public string ExtraStartPath { get => extraStartPath; set { extraStartPath = value; OnPropertyChanged(); } }
        public string ExtraStartArguments { get => extraStartArguments; set { extraStartArguments = value; OnPropertyChanged(); } }

        public string MacroWindowTitle { get => macroWindowTitle; set { macroWindowTitle = value; OnPropertyChanged(); } }

        public ObservableCollection<MacroAction> Actions { get => actions; set { actions = value; OnPropertyChanged(); } }
    }

    public class AppSettings : ObservableObject
    {
        private string barkUrl = "";
        private string barkIcon = "";
        private string screenshotPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Screenshots");
        private string logPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Logs");
        private string onCompletionAction = "No Action";
        private ObservableCollection<TaskItem> taskList = new ObservableCollection<TaskItem>();

        public double MiniLogWidth { get; set; } = 380;
        public double MiniLogHeight { get; set; } = 220;
        public double MiniLogTop { get; set; } = -1;
        public double MiniLogLeft { get; set; } = -1;

        public string BarkUrl { get => barkUrl; set { barkUrl = value; OnPropertyChanged(); } }
        public string BarkIcon { get => barkIcon; set { barkIcon = value; OnPropertyChanged(); } }
        public string ScreenshotPath { get => screenshotPath; set { screenshotPath = value; OnPropertyChanged(); } }
        public string LogPath { get => logPath; set { logPath = value; OnPropertyChanged(); } }
        public string OnCompletionAction { get => onCompletionAction; set { onCompletionAction = value; OnPropertyChanged(); } }
        public ObservableCollection<TaskItem> TaskList { get => taskList; set { taskList = value; OnPropertyChanged(); } }
    }

    public class LogEntry
    {
        public string Time { get; set; } = "";
        public string Message { get; set; } = "";
        public string Color { get; set; } = "Black";
    }
}