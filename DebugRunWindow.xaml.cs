using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using TaskSchedulerApp.Models;

namespace TaskSchedulerApp
{
    public partial class DebugRunWindow : HandyControl.Controls.Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // 基础字段
        private bool _useArgs = true;
        private bool _smartHide = false;
        private bool _enableBark = true;
        private bool _enableScreenshot = true;
        private bool _enableKill = true;

        //窗口卡死检测字段
        private bool _enableZombieCheck;
        private int _zombieTimeout;
        private bool _isTitleValid;

        //属性封装
        public bool UseArgs { get => _useArgs; set { _useArgs = value; OnPropertyChanged(); } }
        public bool SmartHide { get => _smartHide; set { _smartHide = value; OnPropertyChanged(); } }
        public bool EnableBark { get => _enableBark; set { _enableBark = value; OnPropertyChanged(); } }
        public bool EnableScreenshot { get => _enableScreenshot; set { _enableScreenshot = value; OnPropertyChanged(); } }
        public bool EnableKill { get => _enableKill; set { _enableKill = value; OnPropertyChanged(); } }

        public bool EnableZombieCheck { get => _enableZombieCheck; set { _enableZombieCheck = value; OnPropertyChanged(); } }
        public int ZombieTimeout { get => _zombieTimeout; set { _zombieTimeout = value; OnPropertyChanged(); } }
        public bool IsTitleValid { get => _isTitleValid; set { _isTitleValid = value; OnPropertyChanged(); } }


        public DebugRunWindow(TaskItem task)
        {
            InitializeComponent();

            if (task != null)
            {
                IsTitleValid = !string.IsNullOrWhiteSpace(task.ZombieWindowTitle);

                if (IsTitleValid)
                {
                    EnableZombieCheck = task.IsZombieCheckEnabled;
                    ZombieTimeout = task.ZombieCheckTimeout;
                }
                else
                {
                    EnableZombieCheck = false;
                    ZombieTimeout = 5;
                }
            }
            else
            {
                IsTitleValid = false;
                EnableZombieCheck = false;
                ZombieTimeout = 5;
            }

            DataContext = this;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}