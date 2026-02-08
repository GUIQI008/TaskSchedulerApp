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
        private int posX = 0;
        private int posY = 0;
        private string status = "等待中";
        private string extraStartPath = "";
        private string extraStartArguments = "";
        private bool isZombieCheckEnabled = false;
        private string zombieWindowTitle = "";
        private string zombieProcessName = "";
        private int zombieCheckTimeout = 5;

        public string Name { get => name; set { name = value; OnPropertyChanged(); } }
        public string Path { get => path; set { path = value; OnPropertyChanged(); } }
        public string Arguments { get => arguments; set { arguments = value; OnPropertyChanged(); } }
        public string WindowTitle { get => windowTitle; set { windowTitle = value; OnPropertyChanged(); } }

        public int RecognitionTimeout { get => recognitionTimeout; set { recognitionTimeout = value; OnPropertyChanged(); } }
        public string ProcessNames { get => processNames; set { processNames = value; OnPropertyChanged(); } }

        public string ExtraProcessNames { get => extraProcessNames; set { extraProcessNames = value; OnPropertyChanged(); } }
        public int RunTime { get => runTime; set { runTime = value; OnPropertyChanged(); } }
        public int PosX { get => posX; set { posX = value; OnPropertyChanged(); } }
        public int PosY { get => posY; set { posY = value; OnPropertyChanged(); } }
        public string Status { get => status; set { status = value; OnPropertyChanged(); } }
        public string ExtraStartPath { get => extraStartPath; set { extraStartPath = value; OnPropertyChanged(); } }
        public string ExtraStartArguments { get => extraStartArguments; set { extraStartArguments = value; OnPropertyChanged(); } }
        public bool IsZombieCheckEnabled { get => isZombieCheckEnabled; set { isZombieCheckEnabled = value; OnPropertyChanged(); } }
        public string ZombieWindowTitle { get => zombieWindowTitle; set { zombieWindowTitle = value; OnPropertyChanged(); } }
        public string ZombieProcessName { get => zombieProcessName; set { zombieProcessName = value; OnPropertyChanged(); } }
        public int ZombieCheckTimeout { get => zombieCheckTimeout; set { zombieCheckTimeout = value; OnPropertyChanged(); } }
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