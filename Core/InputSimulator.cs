using TaskSchedulerApp.Core;
using TaskSchedulerApp.Models;

namespace TaskSchedulerApp.Services
{
    public static class InputSimulator
    {
        public static void ExecuteAction(MacroAction action)
        {
            switch (action.ActionType)
            {
                case MacroActionType.Delay:
                    // 仅延迟，在外层的 await Task.Delay 已经处理了
                    break;
                case MacroActionType.MouseMove:
                    NativeMethods.SetCursorPos(action.X, action.Y);
                    break;
                case MacroActionType.MouseLeftDown:
                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    break;
                case MacroActionType.MouseLeftUp:
                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    break;
                case MacroActionType.MouseRightDown:
                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                    break;
                case MacroActionType.MouseRightUp:
                    NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                    break;
                case MacroActionType.KeyDown:
                    NativeMethods.keybd_event((byte)action.KeyCode, 0, 0, 0);
                    break;
                case MacroActionType.KeyUp:
                    NativeMethods.keybd_event((byte)action.KeyCode, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
                    break;
            }
        }
    }
}