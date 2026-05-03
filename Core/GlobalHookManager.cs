using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TaskSchedulerApp.Core;

namespace TaskSchedulerApp.Services
{
    public class GlobalKeyEventArgs : EventArgs
    {
        public int KeyCode { get; set; }
        public bool Handled { get; set; }
    }

    public class GlobalMouseEventArgs : EventArgs
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string EventType { get; set; } = ""; // "Down", "Up", "Move"
        public string Button { get; set; } = "";    // "Left", "Right"
        public bool Handled { get; set; }
    }

    public class GlobalHookManager : IDisposable
    {
        private bool _isInstalled;

        // 强引用委托，防止 GC 回收导致程序闪退
        private NativeMethods.HookProc? _mouseProc;
        private NativeMethods.HookProc? _keyboardProc;

        private IntPtr _mouseHookId = IntPtr.Zero;
        private IntPtr _keyboardHookId = IntPtr.Zero;

        public event EventHandler<GlobalMouseEventArgs>? MouseEvent;
        public event EventHandler<GlobalKeyEventArgs>? KeyEvent;

        public void Install()
        {
            if (_isInstalled) return;

            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule!)
            {
                IntPtr moduleHandle = NativeMethods.GetModuleHandle(curModule.ModuleName);

                _mouseProc = MouseHookCallback;
                _mouseHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, moduleHandle, 0);

                _keyboardProc = KeyboardHookCallback;
                _keyboardHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
            }

            _isInstalled = true;
        }

        public void Uninstall()
        {
            if (!_isInstalled) return;

            if (_mouseHookId != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_mouseHookId);
            if (_keyboardHookId != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_keyboardHookId);

            _mouseHookId = IntPtr.Zero;
            _keyboardHookId = IntPtr.Zero;
            _isInstalled = false;
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && MouseEvent != null)
            {
                var hookStruct = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();

                var args = new GlobalMouseEventArgs { X = hookStruct.pt.x, Y = hookStruct.pt.y };

                switch (msg)
                {
                    case NativeMethods.WM_MOUSEMOVE: args.EventType = "Move"; break;
                    case NativeMethods.WM_LBUTTONDOWN: args.EventType = "Down"; args.Button = "Left"; break;
                    case NativeMethods.WM_LBUTTONUP: args.EventType = "Up"; args.Button = "Left"; break;
                    case NativeMethods.WM_RBUTTONDOWN: args.EventType = "Down"; args.Button = "Right"; break;
                    case NativeMethods.WM_RBUTTONUP: args.EventType = "Up"; args.Button = "Right"; break;
                }

                if (!string.IsNullOrEmpty(args.EventType))
                {
                    MouseEvent.Invoke(this, args);
                    if (args.Handled) return new IntPtr(1); // 拦截事件
                }
            }
            return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && KeyEvent != null)
            {
                int msg = wParam.ToInt32();
                if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN ||
                    msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
                {
                    var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                    var args = new GlobalKeyEventArgs { KeyCode = (int)hookStruct.vkCode };

                    KeyEvent.Invoke(this, args);
                    if (args.Handled) return new IntPtr(1); // 拦截事件
                }
            }
            return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Uninstall();
            GC.SuppressFinalize(this);
        }

        ~GlobalHookManager() => Uninstall();
    }
}