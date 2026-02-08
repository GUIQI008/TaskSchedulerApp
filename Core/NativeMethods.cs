using System.Runtime.InteropServices;
using System.Text;

namespace TaskSchedulerApp.Core
{
    public static class NativeMethods
    {
        //窗口查找
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        public static bool IsWindowOpen(string title)
        {
            IntPtr hwnd = FindWindow(null, title);
            if (hwnd == IntPtr.Zero) return false;
            return IsWindowVisible(hwnd);
        }

        //窗口操作
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
            IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_TOPMOST = 0x00000008L;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const int SW_RESTORE = 9;


        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        //鼠标模拟
        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        public static void ClickLeft(int x, int y)
        {
            //移动
            SetCursorPos(x, y);
            Thread.Sleep(200);

            //按下
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            Thread.Sleep(200);

            //抬起
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
            Thread.Sleep(150);
        }

        //辅助功能
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(Point Point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        public const int GA_ROOT = 2;

        public static string GetWindowTitleFromPoint(int x, int y)
        {
            try
            {
                IntPtr hwnd = WindowFromPoint(new Point(x, y));
                if (hwnd == IntPtr.Zero) return "";
                IntPtr rootHwnd = GetAncestor(hwnd, GA_ROOT);
                if (rootHwnd != IntPtr.Zero) hwnd = rootHwnd;
                StringBuilder sb = new StringBuilder(256);
                GetWindowText(hwnd, sb, 256);
                return sb.ToString();
            }
            catch { return ""; }
        }
    }
}