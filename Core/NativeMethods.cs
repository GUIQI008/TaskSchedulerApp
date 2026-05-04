// NativeMethods.cs 修改后
#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskSchedulerApp.Core
{
    public static class NativeMethods
    {
        #region 窗口查找与操作
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const int SW_RESTORE = 9;
        #endregion

        #region 坐标转换 API
        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hWnd, ref System.Drawing.Point lpPoint);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref System.Drawing.Point lpPoint);
        #endregion

        #region 键鼠模拟 API (用于回放)
        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        #endregion

        #region 辅助功能
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(System.Drawing.Point Point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        public const int GA_ROOT = 2;

        public static string GetWindowTitleFromPoint(int x, int y)
        {
            try
            {
                IntPtr hwnd = WindowFromPoint(new System.Drawing.Point(x, y));
                if (hwnd == IntPtr.Zero) return "";
                IntPtr rootHwnd = GetAncestor(hwnd, GA_ROOT);
                if (rootHwnd != IntPtr.Zero) hwnd = rootHwnd;

                StringBuilder sb = new StringBuilder(256);
                GetWindowText(hwnd, sb, 256);
                return sb.ToString();
            }
            catch { return ""; }
        }
        #endregion
    }
}