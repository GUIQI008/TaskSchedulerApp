using System.Threading;
using TaskSchedulerApp.Core;
using TaskSchedulerApp.Models;

namespace TaskSchedulerApp.Services
{
    public static class InputSimulator
    {
        public static void ExecuteAction(MacroAction action)
        {
            NativeMethods.SetCursorPos(action.X, action.Y);
            Thread.Sleep(50); // 移动后微小停顿

            if (action.ActionType == MacroActionType.MouseLeftClick)
            {
                NativeMethods.mouse_event(0x0002, 0, 0, 0, 0); // LBUTTONDOWN
                Thread.Sleep(50); // 拟真按压时间
                NativeMethods.mouse_event(0x0004, 0, 0, 0, 0); // LBUTTONUP
            }
            else if (action.ActionType == MacroActionType.MouseRightClick)
            {
                NativeMethods.mouse_event(0x0008, 0, 0, 0, 0); // RBUTTONDOWN
                Thread.Sleep(50);
                NativeMethods.mouse_event(0x0010, 0, 0, 0, 0); // RBUTTONUP
            }
        }
    }
}