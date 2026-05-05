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

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        public static IntPtr FindWindowFuzzy(string partialTitle)
        {
            if (string.IsNullOrWhiteSpace(partialTitle)) return IntPtr.Zero;

            IntPtr foundHwnd = IntPtr.Zero;

            // 遍历系统中所有顶层窗口
            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd)) // 仅扫描可见窗口
                {
                    int length = GetWindowTextLength(hWnd);
                    if (length > 0)
                    {
                        StringBuilder sb = new StringBuilder(length + 1);
                        GetWindowText(hWnd, sb, sb.Capacity);
                        string title = sb.ToString();

                        // 忽略大小写进行包含匹配
                        if (title.IndexOf(partialTitle, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            foundHwnd = hWnd;
                            return false; // 找到了，停止遍历
                        }
                    }
                }
                return true; // 没找到，继续遍历下一个
            }, IntPtr.Zero);

            return foundHwnd;
        }

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const int SW_RESTORE = 9;
        #endregion

        #region 窗口截图
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hDC, uint nFlags);

        public const uint PW_RENDERFULLCONTENT = 0x00000002;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
        #endregion

        #region 鼠标模拟
        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        public static void ClickLeft(int x, int y)
        {
            SetCursorPos(x, y);
            Thread.Sleep(200);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            Thread.Sleep(200);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
            Thread.Sleep(150);
        }
        #endregion

        #region 辅助功能
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessWorkingSetSize(IntPtr process, int minimumWorkingSetSize, int maximumWorkingSetSize);
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
            catch
            {
                return "";
            }
        }
        #endregion
    }
}