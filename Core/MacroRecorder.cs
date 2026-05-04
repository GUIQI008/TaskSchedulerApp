using System;
using System.Drawing;
using System.Windows.Forms;
using TaskSchedulerApp.Core;
using TaskSchedulerApp.Models;

namespace TaskSchedulerApp.Services
{
    public enum RecordStatus
    {
        Idle,
        Recording
    }

    public class MacroRecorder
    {
        private readonly TaskItem _task;
        private IntPtr _scriptWindowHandle;

        public RecordStatus Status { get; private set; } = RecordStatus.Idle;

        public MacroRecorder(TaskItem task)
        {
            _task = task ?? throw new ArgumentNullException(nameof(task));
        }

        public void Start()
        {
            if (string.IsNullOrWhiteSpace(_task.MacroWindowTitle))
                throw new InvalidOperationException("请先设置脚本窗口标题。");

            IntPtr hwnd = NativeMethods.FindWindow(null, _task.MacroWindowTitle);
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException($"未找到窗口标题为 \"{_task.MacroWindowTitle}\" 的脚本窗口。");

            _scriptWindowHandle = hwnd;
            Status = RecordStatus.Recording;
        }

        public void Stop()
        {
            Status = RecordStatus.Idle;
            _scriptWindowHandle = IntPtr.Zero;
        }

        /// <summary>
        /// 录制一个鼠标点击动作。
        /// </summary>
        /// <param name="button">"Left" 或 "Right"</param>
        /// <param name="screenPoint">屏幕绝对坐标</param>
        /// <returns>是否成功录制</returns>
        public bool RecordClick(string button, Point screenPoint)
        {
            if (Status != RecordStatus.Recording)
                return false;

            // 校验鼠标位置是否在脚本窗口内
            IntPtr hwndUnderCursor = NativeMethods.WindowFromPoint(screenPoint);
            if (hwndUnderCursor == IntPtr.Zero)
                return false;

            // 获取点击位置的根窗口（处理子窗口）
            IntPtr rootHwnd = NativeMethods.GetAncestor(hwndUnderCursor, NativeMethods.GA_ROOT);
            if (rootHwnd == IntPtr.Zero)
                rootHwnd = hwndUnderCursor;

            if (rootHwnd != _scriptWindowHandle)
                return false; // 不在目标脚本窗口上

            // 转换为客户区坐标
            var clientPoint = screenPoint;
            if (!NativeMethods.ScreenToClient(_scriptWindowHandle, ref clientPoint))
                return false;

            // 创建动作序列：移动+按下+抬起
            _task.Actions.Add(new MacroAction
            {
                ActionType = button == "Left" ? MacroActionType.MouseLeftDown : MacroActionType.MouseRightDown,
                X = clientPoint.X,
                Y = clientPoint.Y,
                DelayBefore = 300, // 默认点击前等待 300ms
                Description = button == "Left" ? "左键点击" : "右键点击"
            });
            return true;
        }
    
    }
}