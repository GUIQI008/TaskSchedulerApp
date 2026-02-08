using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Drawing.Imaging;
using TaskSchedulerApp.Core;
using TaskSchedulerApp.Models;

namespace TaskSchedulerApp.Services
{
    public class AutomationService
    {
        private readonly Action<string, string> _uiLogAction;
        private readonly AppSettings _settings;
        private volatile bool _stopRequested = false;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private int _currentTaskId = -1;
        private readonly object _logLock = new object();

        public bool IsStopRequested => _stopRequested;

        public AutomationService(AppSettings settings, Action<string, string> logAction)
        {
            _settings = settings;
            _uiLogAction = logAction;
            CleanOldLogs();
            CleanOldScreenshots();
        }

        public void SetCurrentTaskId(int id) => _currentTaskId = id;
        public void RequestStop() => _stopRequested = true;

        //日志与清理
        private void Log(string type, string message)
        {
            _uiLogAction?.Invoke(type, message);
            try
            {
                if (!Directory.Exists(_settings.LogPath)) Directory.CreateDirectory(_settings.LogPath);
                string fileName = Path.Combine(_settings.LogPath, $"{DateTime.Now:yyyy-MM-dd}.log");
                string logLine = $"[{DateTime.Now:HH:mm:ss}] [{type}] {message}";
                lock (_logLock) { File.AppendAllText(fileName, logLine + Environment.NewLine); }
            }
            catch { }
        }
        private void CleanOldLogs() { Task.Run(() => { try { if (Directory.Exists(_settings.LogPath)) foreach (var f in Directory.GetFiles(_settings.LogPath, "*.log")) if (new FileInfo(f).CreationTime < DateTime.Now.AddHours(-24)) File.Delete(f); } catch { } }); }
        private void CleanOldScreenshots() { Task.Run(() => { try { if (Directory.Exists(_settings.ScreenshotPath)) foreach (var f in Directory.GetFiles(_settings.ScreenshotPath)) if (new FileInfo(f).CreationTime < DateTime.Now.AddHours(-24)) File.Delete(f); } catch { } }); }

        //队列运行
        public async Task RunAllTasks(TaskItem? startFrom = null)
        {
            _stopRequested = false;
            Log("系统", "开始执行任务列表...");
            int total = _settings.TaskList.Count;
            int startIdx = startFrom != null ? Math.Max(0, _settings.TaskList.IndexOf(startFrom)) : 0;

            for (int i = startIdx; i < total; i++)
            {
                if (_stopRequested) { Log("系统", "停止指令已接收，停止队列。"); break; }
                var task = _settings.TaskList[i];
                Log("系统", $">>> 任务 {i + 1}/{total}: {task.Name}");

                await RunSingleTask(task);

                if (!_stopRequested && i < total - 1)
                {
                    Log("系统", "等待 5 秒后执行下一任务...");
                    await Task.Delay(5000);
                }
            }
            if (!_stopRequested) { Log("系统", "全部任务完成。"); PerformPostCompletionAction(); }
            else Log("系统", "任务循环已停止。");
        }

        //单任务逻辑
        public async Task RunSingleTask(TaskItem task)
        {
            Bitmap? lastScreenSample = null;
            DateTime lastScreenChangeTime = DateTime.Now;
            _currentTaskId = -1;

            try
            {
                task.Status = "准备启动";
                CleanOldScreenshots();
                await SendBark(task.Name, "启动中...");

                //额外启动
                if (!string.IsNullOrWhiteSpace(task.ExtraStartPath) && File.Exists(task.ExtraStartPath))
                {
                    Log("系统", $"执行额外启动: {Path.GetFileName(task.ExtraStartPath)}");
                    try
                    {
                        var extraPsi = new ProcessStartInfo { FileName = task.ExtraStartPath, Arguments = task.ExtraStartArguments, UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(task.ExtraStartPath) ?? "" };
                        Process.Start(extraPsi);
                        await Task.Delay(2000);
                    }
                    catch (Exception ex) { Log("警告", $"额外启动出错: {ex.Message}"); }
                }

                //启动主程序
                if (!await AttemptLaunch(task))
                {
                    Log("错误", $"{task.Name} 启动失败 (未检测到窗口或进程)。跳过。");
                    KillRelatedProcesses(task);
                    task.Status = "启动失败";
                    return;
                }

                Log("任务", "启动成功，等待3秒稳定...");
                await Task.Delay(3000);

                //模拟点击
                if (task.PosX != 0 || task.PosY != 0)
                {
                    Log("操作", $"执行点击: ({task.PosX}, {task.PosY})");
                    NativeMethods.ClickLeft(task.PosX, task.PosY);
                }

                task.Status = "运行中";
                Log("任务", $"开始监控，时长: {task.RunTime}分钟");
                await SendBark(task.Name, "运行中");

                DateTime endTime = DateTime.Now.AddMinutes(task.RunTime);
                bool screenshotTaken = false;
                int missingCounter = 0;

                //监控循环
                while (DateTime.Now < endTime && !_stopRequested)
                {
                    // 存活检测
                    if (!CheckIfTaskIsAlive(task))
                    {
                        missingCounter++;
                        int tolerance = Math.Max(task.RecognitionTimeout, 5);
                        if (missingCounter % 5 == 0 || missingCounter == 1)
                            Log("监控", $"目标丢失，确认状态中... ({missingCounter}/{tolerance}s)");

                        if (missingCounter >= tolerance)
                        {
                            Log("监控", $"{task.Name} 主进程退出，任务结束。");
                            KillRelatedProcesses(task);
                            task.Status = "已完成";
                            await SendBark(task.Name, "运行结束");
                            return;
                        }
                    }
                    else
                    {
                        if (missingCounter > 0) Log("监控", "目标进程已恢复。");
                        missingCounter = 0;
                    }

                    if (task.IsZombieCheckEnabled)
                    {
                        IntPtr zombieHwnd = FindZombieTargetHwnd(task);

                        if (zombieHwnd != IntPtr.Zero)
                        {
                            NativeMethods.SetForegroundWindow(zombieHwnd);
                            NativeMethods.SetWindowPos(zombieHwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);

                            // 获取截图
                            Bitmap? currentSample = GetScreenSample(zombieHwnd);
                            if (currentSample != null)
                            {
                                if (lastScreenSample != null && AreBitmapsIdentical(lastScreenSample, currentSample))
                                {
                                    if ((DateTime.Now - lastScreenChangeTime).TotalMinutes >= task.ZombieCheckTimeout)
                                    {
                                        Log("监控", $"检测到画面卡死 {task.ZombieCheckTimeout} 分钟。终止任务。");
                                        KillRelatedProcesses(task);
                                        task.Status = "卡死跳过";
                                        lastScreenSample?.Dispose();
                                        currentSample?.Dispose();
                                        await SendBark(task.Name, "卡死终止");
                                        return;
                                    }
                                }
                                else lastScreenChangeTime = DateTime.Now;

                                lastScreenSample?.Dispose();
                                lastScreenSample = currentSample;
                            }
                        }
                    }

                    //  截图
                    if ((endTime - DateTime.Now).TotalSeconds <= 60 && !screenshotTaken)
                    {
                        TakeScreenshot(task);
                        screenshotTaken = true;
                    }

                    await Task.Delay(1000);
                }

                if (lastScreenSample != null) lastScreenSample.Dispose();

                if (_stopRequested)
                {
                    Log("系统", "用户手动停止。");
                    task.Status = "已停止";
                    KillRelatedProcesses(task);
                    await SendBark(task.Name, "用户停止");
                }
                else
                {
                    Log("任务", "运行时间结束，正常关闭。");
                    KillRelatedProcesses(task);
                    task.Status = "完成";
                    await SendBark(task.Name, "完成");
                }
            }
            catch (Exception ex)
            {
                Log("错误", $"任务异常: {ex.Message}");
                KillRelatedProcesses(task);
                task.Status = "异常";
            }
            finally { _currentTaskId = -1; }
        }

        // --- 辅助方法 ---
        private IntPtr FindZombieTargetHwnd(TaskItem task)
        {
            // 优先尝试进程名
            if (!string.IsNullOrWhiteSpace(task.ZombieProcessName))
            {
                string cleanName = GetCleanProcessName(task.ZombieProcessName);
                var processes = Process.GetProcessesByName(cleanName);
                if (processes.Length > 0)
                {
                    IntPtr hwnd = processes[0].MainWindowHandle;
                    if (hwnd == IntPtr.Zero)
                    {
                        foreach (var p in processes) if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
                    }
                    if (hwnd != IntPtr.Zero) return hwnd;
                }
            }
            // 尝试窗口标题
            if (!string.IsNullOrWhiteSpace(task.ZombieWindowTitle))
            {
                return NativeMethods.FindWindow(null, task.ZombieWindowTitle);
            }
            // 保底使用启动标题
            if (!string.IsNullOrWhiteSpace(task.WindowTitle))
            {
                return NativeMethods.FindWindow(null, task.WindowTitle);
            }
            return IntPtr.Zero;
        }

        private bool CheckIfTaskIsAlive(TaskItem task)
        {
            if (!string.IsNullOrWhiteSpace(task.ProcessNames))
            {
                if (!AreProcessesDead(task)) return true;
            }
            if (_currentTaskId != -1)
            {
                try { Process.GetProcessById(_currentTaskId); return true; } catch { }
            }
            return false;
        }

        private async Task<bool> AttemptLaunch(TaskItem task)
        {
            try
            {
                // 启动进程
                if (!string.IsNullOrWhiteSpace(task.Path) && File.Exists(task.Path))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = task.Path,
                        Arguments = task.Arguments,
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(task.Path) ?? ""
                    };
                    var p = Process.Start(psi);
                    if (p != null) _currentTaskId = p.Id;
                }
                else
                {
                    return false;
                }
            }
            catch { return false; }

            if (string.IsNullOrWhiteSpace(task.WindowTitle)) return true;

            int requiredStability = 3; 
            int consecutiveCount = 0; 

            // 总超时时间
            for (int i = 0; i < task.RecognitionTimeout; i++)
            {
                if (_stopRequested) return false;

                IntPtr hwnd = NativeMethods.FindWindow(null, task.WindowTitle);

                if (hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd))
                {
                    if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                    NativeMethods.SetForegroundWindow(hwnd);

                    consecutiveCount++;

                    if (consecutiveCount < requiredStability)
                    {
                        Log("系统", $"{task.Name} 窗口已出现，正在确认稳定... ({consecutiveCount}/{requiredStability})");
                    }
                    else
                    {
                        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);

                        return true;
                    }
                }
                else
                {
                    if (consecutiveCount > 0)
                    {
                        Log("警告", $"{task.Name} 窗口信号丢失，重新等待...");
                        consecutiveCount = 0;
                    }
                }

                await Task.Delay(1000);
            }

            return false;
        }

        public Bitmap? GetScreenSample(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;
            try
            {
                if (NativeMethods.IsIconic(hwnd))
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                    Thread.Sleep(200);
                }

                NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect);
                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;
                if (w <= 0 || h <= 0) return null;

                int size = 100;
                if (size > w) size = w;
                if (size > h) size = h;
                if (size <= 0) return null;

                int sx = rect.Left + (w - size) / 2;
                int sy = rect.Top + (h - size) / 2;

                Bitmap bmp = new Bitmap(size, size);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(sx, sy, 0, 0, new Size(size, size));
                }
                return bmp;
            }
            catch { return null; }
        }

        public bool AreBitmapsIdentical(Bitmap? bmp1, Bitmap? bmp2)
        {
            if (bmp1 == null || bmp2 == null || bmp1.Size != bmp2.Size) return false;
            Rectangle rect = new Rectangle(0, 0, bmp1.Width, bmp1.Height);
            BitmapData data1 = bmp1.LockBits(rect, ImageLockMode.ReadOnly, bmp1.PixelFormat);
            BitmapData data2 = bmp2.LockBits(rect, ImageLockMode.ReadOnly, bmp2.PixelFormat);
            bool equal = true;
            try
            {
                int bytes = Math.Abs(data1.Stride) * bmp1.Height;
                byte[] buffer1 = new byte[bytes];
                byte[] buffer2 = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(data1.Scan0, buffer1, 0, bytes);
                System.Runtime.InteropServices.Marshal.Copy(data2.Scan0, buffer2, 0, bytes);
                for (int i = 0; i < bytes; i++)
                {
                    if (buffer1[i] != buffer2[i]) { equal = false; break; }
                }
            }
            catch { equal = false; }
            finally { bmp1.UnlockBits(data1); bmp2.UnlockBits(data2); }
            return equal;
        }

        private string GetCleanProcessName(string raw) => raw?.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase) ?? "";

        private bool AreProcessesDead(TaskItem task)
        {
            var names = task.ProcessNames.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var name in names)
            {
                string cleanName = GetCleanProcessName(name);
                if (string.IsNullOrEmpty(cleanName)) continue;
                try { if (Process.GetProcessesByName(cleanName).Length > 0) return false; } catch { }
            }
            return true;
        }

        public void KillRelatedProcesses(TaskItem task)
        {
            if (_currentTaskId != -1) try { Process.GetProcessById(_currentTaskId).Kill(true); } catch { }
            KillList(task.ProcessNames);
            KillList(task.ExtraProcessNames);
        }

        private void KillList(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv)) return;
            foreach (var name in csv.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string cleanName = GetCleanProcessName(name);
                if (string.IsNullOrEmpty(cleanName)) continue;
                try { foreach (var p in Process.GetProcessesByName(cleanName)) try { p.Kill(true); p.Dispose(); } catch { } } catch { }
            }
        }

        public void TakeScreenshot(TaskItem task)
        {
            try
            {
                if (!Directory.Exists(_settings.ScreenshotPath)) Directory.CreateDirectory(_settings.ScreenshotPath);
                string path = Path.Combine(_settings.ScreenshotPath, $"{task.Name}_{DateTime.Now:MMdd_HHmm}.png");
                var bounds = Screen.PrimaryScreen.Bounds;
                using var bmp = new Bitmap(bounds.Width, bounds.Height);
                using (var g = Graphics.FromImage(bmp)) { g.CopyFromScreen(Point.Empty, Point.Empty, bounds.Size); }
                bmp.Save(path, ImageFormat.Png);
                Log("截图", "已保存");
            }
            catch { }
        }
        public async Task SendBark(string t, string b)
        {
            if (string.IsNullOrWhiteSpace(_settings.BarkUrl)) return;
            try { string url = $"{_settings.BarkUrl.TrimEnd('/')}/{Uri.EscapeDataString(t)}/{Uri.EscapeDataString(b)}"; if (!string.IsNullOrWhiteSpace(_settings.BarkIcon)) url += $"?icon={_settings.BarkIcon}"; await _httpClient.GetAsync(url); } catch { }
        }
        private void PerformPostCompletionAction()
        {
            try
            {
                if (_settings.OnCompletionAction == "Shutdown") Process.Start("shutdown", "/s /t 5");
                else if (_settings.OnCompletionAction == "Sleep") Process.Start("rundll32.exe", "pow-rprof.dll,SetSuspendState 0,1,0");
            }
            catch { }
        }
    }
}