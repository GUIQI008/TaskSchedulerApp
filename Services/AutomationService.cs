#nullable enable

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using TaskSchedulerApp.Core;
using TaskSchedulerApp.Models;
using System.Collections.Generic;
using System.Linq;

namespace TaskSchedulerApp.Services
{
    public class AutomationService
    {
        private readonly Action<string, string> _uiLogAction;
        private readonly AppSettings _settings;
        private volatile bool _stopRequested = false;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private readonly object _logLock = new object();

        // 防刷屏状态字段
        private IntPtr lastLoggedHwnd = IntPtr.Zero;
        private double lastLoggedStagnantMinutes = 0;
        private bool lastAliveState = true;

        public bool IsStopRequested => _stopRequested;

        public AutomationService(AppSettings settings, Action<string, string> logAction)
        {
            _settings = settings;
            _uiLogAction = logAction;
            CleanOldLogs();
            CleanOldScreenshots();
        }

        public void RequestStop() => _stopRequested = true;

        #region 日志与文件清理
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

        private void CleanOldLogs() => Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(_settings.LogPath))
                {
                    foreach (var file in Directory.GetFiles(_settings.LogPath, "*.log"))
                    {
                        if (new FileInfo(file).CreationTime < DateTime.Now.AddHours(-24))
                            File.Delete(file);
                    }
                }
            }
            catch { }
        });

        private void CleanOldScreenshots() => Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(_settings.ScreenshotPath))
                {
                    foreach (var file in Directory.GetFiles(_settings.ScreenshotPath))
                    {
                        if (new FileInfo(file).CreationTime < DateTime.Now.AddHours(-24))
                            File.Delete(file);
                    }
                }
            }
            catch { }
        });
        #endregion

        #region 任务队列执行
        public async Task RunAllTasks(TaskItem? startFrom = null)
        {
            _stopRequested = false;
            Log("系统", "=== 开始执行任务队列 ===");
            int total = _settings.TaskList.Count;
            int startIdx = startFrom != null ? Math.Max(0, _settings.TaskList.IndexOf(startFrom)) : 0;

            for (int i = startIdx; i < total; i++)
            {
                if (_stopRequested) { Log("系统", "收到停止指令，中断队列执行"); break; }

                var task = _settings.TaskList[i];
                Log("系统", $">>> 执行任务 {i + 1}/{total}: {task.Name}");

                await RunSingleTask(task);

                if (!_stopRequested && i < total - 1)
                {
                    Log("系统", "任务间隔：等待 5 秒...");
                    await Task.Delay(5000);
                }
            }

            if (!_stopRequested)
            {
                Log("系统", "=== 所有任务执行完成 ===");
                PerformPostCompletionAction();
            }
        }
        #endregion

        #region 单任务核心执行逻辑
        public async Task RunSingleTask(TaskItem task)
        {
            Bitmap? lastScreenSample = null;
            DateTime lastScreenChangeTime = DateTime.Now;

            try
            {
                task.Status = "准备启动";
                CleanOldScreenshots();
                await SendBark(task.Name, "启动中...");

                #region 额外启动程序
                if (!string.IsNullOrWhiteSpace(task.ExtraStartPath) && File.Exists(task.ExtraStartPath))
                {
                    Log("系统", $"执行额外启动程序: {task.ExtraStartPath} {task.ExtraStartArguments}");
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = task.ExtraStartPath,
                            Arguments = task.ExtraStartArguments,
                            UseShellExecute = true,
                            WorkingDirectory = Path.GetDirectoryName(task.ExtraStartPath) ?? ""
                        };
                        using var process = Process.Start(psi);
                        if (process != null)
                            Log("系统", $"额外程序启动成功，PID: {process.Id}");
                        await Task.Delay(2000);
                    }
                    catch (Exception ex) { Log("警告", $"额外启动失败: {ex.Message}"); }
                }
                #endregion

                #region 启动主程序
                var (launchSuccess, mainProcess) = await AttemptLaunch(task);

                if (!launchSuccess)
                {
                    Log("错误", $"{task.Name} 启动失败（窗口/进程未检测到），跳过任务");
                    KillRelatedProcesses(task);
                    task.Status = "启动失败";
                    return;
                }

                Log("任务", mainProcess != null
                    ? $"主程序启动成功，PID: {mainProcess.Id}，进程名: {mainProcess.ProcessName}"
                    : "主程序启动成功（通过窗口检测确认）");
                #endregion

                Log("任务", "等待 3 秒窗口稳定...");
                await Task.Delay(3000);

                if (task.PosX != 0 || task.PosY != 0)
                {
                    Log("操作", $"模拟点击坐标: ({task.PosX}, {task.PosY})");
                    NativeMethods.ClickLeft(task.PosX, task.PosY);
                }

                task.Status = "运行中";
                Log("任务", $"开始监控运行，时长: {task.RunTime} 分钟");
                await SendBark(task.Name, "运行中");

                DateTime endTime = DateTime.Now.AddMinutes(task.RunTime);
                int missingCounter = 0;

                #region 主监控循环
                while (DateTime.Now < endTime && !_stopRequested)
                {
                    #region 进程存活检测
                    var aliveProcesses = GetAliveProcesses(task);
                    bool currentlyAlive = aliveProcesses.Count > 0;

                    if (currentlyAlive != lastAliveState)
                    {
                        if (currentlyAlive)
                            Log("监控", $"目标进程恢复，当前存活 PID: {string.Join(", ", aliveProcesses.Select(p => p.Id))}");
                        else
                            Log("监控", "目标进程丢失（连续5秒将结束任务）");

                        lastAliveState = currentlyAlive;
                    }

                    if (!currentlyAlive)
                    {
                        missingCounter++;

                        if (missingCounter >= 5)
                        {
                            Log("监控", $"{task.Name} 主进程连续丢失5秒，任务结束");
                            KillRelatedProcesses(task);
                            task.Status = "已完成";
                            await SendBark(task.Name, "运行结束（进程丢失5秒）");
                            return;
                        }
                    }
                    else
                    {
                        missingCounter = 0;
                    }
                    #endregion

                    #region 僵尸窗口检测（极致零日志：只关键事件）
                    if (task.IsZombieCheckEnabled)
                    {
                        IntPtr zombieHwnd = FindZombieTargetHwnd(task);
                        if (zombieHwnd != IntPtr.Zero)
                        {
                            string title = GetWindowTitle(zombieHwnd);

                            if (lastLoggedHwnd != zombieHwnd)
                            {
                                Log("监控", $"僵尸检测目标窗口变更: HWND=0x{zombieHwnd.ToInt64():X}, 标题=\"{title}\"");
                                lastLoggedHwnd = zombieHwnd;
                            }

                            using Bitmap? currentSample = GetScreenSample(zombieHwnd);
                            if (currentSample != null)
                            {
                                if (lastScreenSample != null)
                                {
                                    if (AreBitmapsIdentical(lastScreenSample, currentSample))
                                    {
                                        double stagnantMinutes = (DateTime.Now - lastScreenChangeTime).TotalMinutes;

                                        if (stagnantMinutes >= task.ZombieCheckTimeout)
                                        {
                                            Log("监控", $"画面卡死超过 {task.ZombieCheckTimeout} 分钟，终止任务");
                                            KillRelatedProcesses(task);
                                            task.Status = "卡死终止";
                                            await SendBark(task.Name, "卡死终止");
                                            return;
                                        }
                                    }
                                    else
                                    {
                                        lastScreenChangeTime = DateTime.Now;
                                        lastScreenSample?.Dispose();
                                        lastScreenSample = (Bitmap)currentSample.Clone();
                                    }
                                }
                                else
                                {
                                    Log("监控", "僵尸检测开始采样初始画面");
                                    lastScreenSample = (Bitmap)currentSample.Clone();
                                    lastScreenChangeTime = DateTime.Now;
                                }
                            }
                        }
                        else if (lastLoggedHwnd != IntPtr.Zero)
                        {
                            // 窗口消失时打印一次
                            Log("监控", "僵尸检测目标窗口已消失");
                            lastLoggedHwnd = IntPtr.Zero;
                        }
                    }
                    #endregion

                    await Task.Delay(1000);
                }
                #endregion

                if (!_stopRequested)
                {
                    task.Status = "已完成";
                    Log("任务", "运行时长到达，任务正常结束");
                    await SendBark(task.Name, "运行结束（正常）");
                }
            }
            finally
            {
                lastScreenSample?.Dispose();
            }
        }
        #endregion

        #region 辅助方法
        private async Task<(bool Success, Process? LaunchedProcess)> AttemptLaunch(TaskItem task)
        {
            Process? launchedProcess = null;

            if (string.IsNullOrWhiteSpace(task.Path) || !File.Exists(task.Path))
            {
                Log("错误", "主程序路径无效或不存在");
                return (false, null);
            }

            Log("系统", $"启动主程序: \"{task.Path}\" {task.Arguments}");

            var psi = new ProcessStartInfo
            {
                FileName = task.Path,
                Arguments = task.Arguments,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(task.Path) ?? ""
            };

            try
            {
                launchedProcess = Process.Start(psi);
                if (launchedProcess != null)
                    Log("任务", $"主程序启动成功，PID: {launchedProcess.Id}，进程名: {launchedProcess.ProcessName}");
            }
            catch (Exception ex)
            {
                Log("错误", $"启动进程异常: {ex.Message}");
                return (false, null);
            }

            if (string.IsNullOrWhiteSpace(task.WindowTitle))
                return (true, launchedProcess);

            int requiredStability = 3;
            int consecutiveCount = 0;

            for (int i = 0; i < task.RecognitionTimeout; i++)
            {
                if (_stopRequested) return (false, launchedProcess);

                IntPtr hwnd = NativeMethods.FindWindow(null, task.WindowTitle);
                if (hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd))
                {
                    if (NativeMethods.IsIconic(hwnd))
                        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

                    NativeMethods.SetForegroundWindow(hwnd);
                    consecutiveCount++;

                    if (consecutiveCount >= requiredStability)
                    {
                        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
                        Log("任务", $"窗口稳定检测成功（连续 {requiredStability} 次），已置顶窗口");
                        return (true, launchedProcess);
                    }
                }
                else
                {
                    consecutiveCount = 0;
                }

                await Task.Delay(1000);
            }

            Log("错误", $"窗口标题 \"{task.WindowTitle}\" 在 {task.RecognitionTimeout} 秒内未稳定出现");
            return (false, launchedProcess);
        }

        private List<Process> GetAliveProcesses(TaskItem task)
        {
            var list = new List<Process>();
            var names = task.ProcessNames.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var name in names)
            {
                string clean = name.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrEmpty(clean)) continue;
                try { list.AddRange(Process.GetProcessesByName(clean)); }
                catch { }
            }
            return list;
        }

        private IntPtr FindZombieTargetHwnd(TaskItem task)
        {
            var processes = GetAliveProcesses(task);
            if (processes.Count > 0 && !string.IsNullOrWhiteSpace(task.ZombieProcessName))
            {
                string clean = task.ZombieProcessName.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                var match = processes.FirstOrDefault(p => p.ProcessName.Equals(clean, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match.MainWindowHandle;
            }

            if (!string.IsNullOrWhiteSpace(task.ZombieWindowTitle))
                return NativeMethods.FindWindow(null, task.ZombieWindowTitle);

            return IntPtr.Zero;
        }

        private string GetWindowTitle(IntPtr hwnd)
        {
            var sb = new System.Text.StringBuilder(256);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public void KillRelatedProcesses(TaskItem task)
        {
            var processes = GetAliveProcesses(task);
            if (processes.Count > 0)
            {
                Log("操作", $"终止相关进程: {string.Join(", ", processes.Select(p => $"{p.ProcessName}(PID:{p.Id})"))}");
                foreach (var p in processes)
                {
                    try { p.Kill(true); p.Dispose(); }
                    catch { }
                }
            }

            if (!string.IsNullOrWhiteSpace(task.ExtraProcessNames))
            {
                var extraNames = task.ExtraProcessNames.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var name in extraNames)
                {
                    string clean = name.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                    if (string.IsNullOrEmpty(clean)) continue;
                    try
                    {
                        foreach (var p in Process.GetProcessesByName(clean))
                        {
                            Log("操作", $"终止额外进程: {p.ProcessName}(PID:{p.Id})");
                            p.Kill(true); p.Dispose();
                        }
                    }
                    catch { }
                }
            }
        }

        public Bitmap? GetScreenSample(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;
            try
            {
                NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect);
                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;
                if (w <= 0 || h <= 0) return null;

                using Bitmap fullBmp = new Bitmap(w, h);
                using (Graphics g = Graphics.FromImage(fullBmp))
                {
                    IntPtr hdc = g.GetHdc();
                    try
                    {
                        NativeMethods.PrintWindow(hwnd, hdc, NativeMethods.PW_RENDERFULLCONTENT);
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }

                int size = Math.Min(100, Math.Min(w, h));
                int sx = (w - size) / 2;
                int sy = (h - size) / 2;

                var cropped = new Bitmap(size, size);
                using (Graphics gCrop = Graphics.FromImage(cropped))
                {
                    gCrop.DrawImage(fullBmp, new Rectangle(0, 0, size, size), new Rectangle(sx, sy, size, size), GraphicsUnit.Pixel);
                }
                return cropped;
            }
            catch
            {
                return null;
            }
        }

        private bool AreBitmapsIdentical(Bitmap bmp1, Bitmap bmp2)
        {
            if (bmp1.Size != bmp2.Size) return false;

            BitmapData data1 = bmp1.LockBits(new Rectangle(0, 0, bmp1.Width, bmp1.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            BitmapData data2 = bmp2.LockBits(new Rectangle(0, 0, bmp2.Width, bmp2.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int bytes = Math.Abs(data1.Stride) * bmp1.Height;
                byte[] buffer1 = new byte[bytes];
                byte[] buffer2 = new byte[bytes];

                System.Runtime.InteropServices.Marshal.Copy(data1.Scan0, buffer1, 0, bytes);
                System.Runtime.InteropServices.Marshal.Copy(data2.Scan0, buffer2, 0, bytes);

                for (int i = 0; i < bytes; i++)
                {
                    if (buffer1[i] != buffer2[i]) return false;
                }
                return true;
            }
            finally
            {
                bmp1.UnlockBits(data1);
                bmp2.UnlockBits(data2);
            }
        }

        public void TakeScreenshot(TaskItem task)
        {
            try
            {
                if (!Directory.Exists(_settings.ScreenshotPath)) Directory.CreateDirectory(_settings.ScreenshotPath);
                string path = Path.Combine(_settings.ScreenshotPath, $"{task.Name}_{DateTime.Now:MMdd_HHmmss}.png");
                var bounds = Screen.PrimaryScreen!.Bounds;
                using var bmp = new Bitmap(bounds.Width, bounds.Height);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(System.Drawing.Point.Empty, System.Drawing.Point.Empty, bounds.Size);
                }
                bmp.Save(path, ImageFormat.Png);
                Log("截图", $"全屏截图已保存: {path}");
            }
            catch (Exception ex)
            {
                Log("错误", $"截图失败: {ex.Message}");
            }
        }

        public async Task SendBark(string title, string body)
        {
            if (string.IsNullOrWhiteSpace(_settings.BarkUrl)) return;
            try
            {
                string url = $"{_settings.BarkUrl.TrimEnd('/')}/{Uri.EscapeDataString(title)}/{Uri.EscapeDataString(body)}";
                if (!string.IsNullOrWhiteSpace(_settings.BarkIcon))
                    url += $"?icon={Uri.EscapeDataString(_settings.BarkIcon)}";
                await _httpClient.GetAsync(url);
            }
            catch { }
        }

        private void PerformPostCompletionAction()
        {
            try
            {
                if (_settings.OnCompletionAction == "Shutdown")
                    Process.Start("shutdown", "/s /t 5");
                else if (_settings.OnCompletionAction == "Sleep")
                    Process.Start("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");
            }
            catch { }
        }
        #endregion
    }
}