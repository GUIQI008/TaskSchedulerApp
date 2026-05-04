#nullable enable

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
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

        // 【已修复】：删除了全局的 lastAliveState，防止多个任务之间状态污染

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
            var executionQueue = _settings.TaskList.ToList();
            int total = executionQueue.Count;
            int startIdx = 0;
            if (startFrom != null)
            {
                int originalIndex = _settings.TaskList.IndexOf(startFrom);
                if (originalIndex >= 0) startIdx = originalIndex;
            }

            for (int i = startIdx; i < total; i++)
            {
                if (_stopRequested) { Log("系统", "收到停止指令，中断队列执行"); break; }

                var task = executionQueue[i];
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
            Process? mainProcess = null;

            try
            {
                task.Status = "准备启动";
                CleanOldScreenshots();
                await SendBark(task.Name, "启动中...");

                // 1. 先启动游戏(额外程序)并等待游戏窗口
                if (!string.IsNullOrWhiteSpace(task.ExtraStartPath) && File.Exists(task.ExtraStartPath))
                {
                    Log("系统", $"启动游戏: {task.ExtraStartPath}");
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = task.ExtraStartPath, Arguments = task.ExtraStartArguments, UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(task.ExtraStartPath) ?? "" });
                        if (!string.IsNullOrWhiteSpace(task.WindowTitle))
                        {
                            Log("任务", $"等待游戏窗口: [{task.WindowTitle}]");
                            if (!await WaitForWindow(task.WindowTitle, task.RecognitionTimeout))
                                Log("错误", "游戏窗口检测超时！(将继续往下执行)");
                        }
                    }
                    catch (Exception ex) { Log("警告", $"游戏启动失败: {ex.Message}"); }
                }

                // 2. 后启动脚本(主程序)并等待脚本窗口
                if (!string.IsNullOrWhiteSpace(task.Path) && File.Exists(task.Path))
                {
                    Log("系统", $"启动脚本: {task.Path}");
                    try
                    {
                        mainProcess = Process.Start(new ProcessStartInfo { FileName = task.Path, Arguments = task.Arguments, UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(task.Path) ?? "" });

                        // 【这里注意】：你的 task.ScriptRecognitionTimeout 我没看到定义，为了避免编译报错，我改用 task.RecognitionTimeout。
                        // 如果你定义了 ScriptRecognitionTimeout，请把下面这行的 RecognitionTimeout 改回 ScriptRecognitionTimeout
                        if (!string.IsNullOrWhiteSpace(task.ScriptWindowTitle))
                        {
                            Log("任务", $"等待脚本窗口: [{task.ScriptWindowTitle}]");
                            if (!await WaitForWindow(task.ScriptWindowTitle, task.RecognitionTimeout))
                            {
                                Log("错误", "脚本窗口检测超时，任务中断");
                                KillRelatedProcesses(task, mainProcess);
                                task.Status = "启动失败";
                                return;
                            }
                        }
                    }
                    catch (Exception ex) { Log("错误", $"脚本启动异常: {ex.Message}"); return; }
                }

                Log("任务", "程序加载完毕，等待 3 秒稳定...");
                await Task.Delay(3000);

                // 3. 执行宏录制动作（包含防遮挡）
                if (task.Actions != null && task.Actions.Count > 0)
                {
                    Log("操作", $"开始执行宏操作，共 {task.Actions.Count} 步...");

                    IntPtr targetHwnd = IntPtr.Zero;
                    if (!string.IsNullOrWhiteSpace(task.ScriptWindowTitle))
                    {
                        targetHwnd = NativeMethods.FindWindow(null, task.ScriptWindowTitle);
                        if (targetHwnd != IntPtr.Zero)
                        {
                            Log("系统", "防遮挡：已将【脚本窗口】强制置顶");
                            if (NativeMethods.IsIconic(targetHwnd)) NativeMethods.ShowWindow(targetHwnd, NativeMethods.SW_RESTORE);
                            NativeMethods.SetForegroundWindow(targetHwnd);
                            NativeMethods.SetWindowPos(targetHwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
                        }
                    }

                    foreach (var action in task.Actions)
                    {
                        if (_stopRequested) break;
                        if (action.DelayBefore > 0) await Task.Delay(action.DelayBefore);

                        if (targetHwnd != IntPtr.Zero) NativeMethods.SetForegroundWindow(targetHwnd); // 点击前强校验
                        InputSimulator.ExecuteAction(action);
                    }

                    if (targetHwnd != IntPtr.Zero)
                    {
                        Log("系统", "防遮挡：已恢复正常层级");
                        NativeMethods.SetWindowPos(targetHwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
                    }
                    Log("操作", "宏操作执行完毕！");
                }

                // 4. 进程存活监控循环
                task.Status = "运行中";
                Log("任务", $"开始监控运行，时长: {task.RunTime} 分钟");
                DateTime endTime = DateTime.Now.AddMinutes(task.RunTime);

                // 【核心修复】：全部采用局部变量
                int missingCounter = 0;
                bool lastAliveState = true;
                bool hasTakenScreenshot = false;
                bool hasDetectedOnce = false; // 宏宽限期：只要探测到一次，才允许判断丢失

                while (DateTime.Now < endTime && !_stopRequested)
                {
                    // 仅当配置了进程名时才去监控死活，否则纯挂机
                    if (!string.IsNullOrWhiteSpace(task.ProcessNames) || !string.IsNullOrWhiteSpace(task.ExtraProcessNames))
                    {
                        // 传入 mainProcess 配合新版强效判定
                        var aliveProcesses = GetAliveProcesses(task, mainProcess);
                        bool currentlyAlive = aliveProcesses.Count > 0;

                        if (currentlyAlive) hasDetectedOnce = true;

                        if (currentlyAlive != lastAliveState)
                        {
                            Log("监控", currentlyAlive ? $"目标进程存在/恢复" : "目标进程丢失（连续5秒后将结束任务）");
                            lastAliveState = currentlyAlive;
                        }

                        if (!currentlyAlive)
                        {
                            // 必须曾探测到进程存活，才开始计算丢失
                            // 防止宏还没把游戏启动起来就被判负
                            if (hasDetectedOnce)
                            {
                                missingCounter++;
                                if (missingCounter >= 5)
                                {
                                    Log("监控", $"{task.Name} 进程彻底丢失，结束当前任务");
                                    break; // 【核心修复】：用 break 跳出，保证走下面的收尾和截图逻辑
                                }
                            }
                        }
                        else
                        {
                            missingCounter = 0; // 只要活着，重置计数
                        }
                    }

                    // 自动截图逻辑 (最后 60 秒)
                    if (!hasTakenScreenshot && (endTime - DateTime.Now).TotalSeconds <= 60)
                    {
                        Log("任务", "任务进入最后一分钟，执行自动截图...");
                        TakeScreenshot(task);
                        hasTakenScreenshot = true;
                    }

                    await Task.Delay(1000);
                }

                // 5. 结束收尾
                // 【核心修复】：保底截图，防用户设定的时间太短或因进程丢失跳过截图
                if (!hasTakenScreenshot && !_stopRequested)
                {
                    Log("任务", "执行最终检查截图...");
                    TakeScreenshot(task);
                }

                task.Status = _stopRequested ? "手动停止" : "已完成";
                KillRelatedProcesses(task, mainProcess);
                await SendBark(task.Name, task.Status);
            }
            finally
            {
                KillRelatedProcesses(task, mainProcess);
            }
        }

        // 辅助检测窗口稳定的方法
        private async Task<bool> WaitForWindow(string title, int timeoutSeconds)
        {
            int consecutiveCount = 0;
            for (int i = 0; i < timeoutSeconds; i++)
            {
                if (_stopRequested) return false;
                IntPtr hwnd = NativeMethods.FindWindow(null, title);
                if (hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd))
                {
                    consecutiveCount++;
                    if (consecutiveCount >= 2) return true; // 连续2秒找到视为稳定
                }
                else consecutiveCount = 0;
                await Task.Delay(1000);
            }
            return false;
        }
        #endregion

        #region 辅助方法
        // 【核心修复】：升级版 GetAliveProcesses，严厉打击僵尸进程！
        private List<Process> GetAliveProcesses(TaskItem task, Process? mainProcess)
        {
            var list = new List<Process>();
            var allNames = new List<string>();

            if (!string.IsNullOrWhiteSpace(task.ProcessNames))
                allNames.AddRange(task.ProcessNames.Split(',', StringSplitOptions.RemoveEmptyEntries));
            if (!string.IsNullOrWhiteSpace(task.ExtraProcessNames))
                allNames.AddRange(task.ExtraProcessNames.Split(',', StringSplitOptions.RemoveEmptyEntries));

            foreach (var name in allNames)
            {
                string clean = name.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrEmpty(clean)) continue;
                try
                {
                    foreach (var p in Process.GetProcessesByName(clean))
                    {
                        try
                        {
                            // 必须检查 HasExited！过滤掉上一个任务残留的僵尸句柄
                            if (!p.HasExited) list.Add(p);
                        }
                        catch { list.Add(p); } // 遇到拒绝访问的情况默认认为它还活着
                    }
                }
                catch { }
            }

            // 【兜底修复】：如果按名字没找到（或者没填名字），但主进程对象还活着，也算作存活
            if (list.Count == 0 && mainProcess != null)
            {
                try { if (!mainProcess.HasExited) list.Add(mainProcess); } catch { }
            }

            return list;
        }

        private void KillRelatedProcesses(TaskItem task, Process? mainProcess = null)
        {
            if (mainProcess != null)
            {
                try
                {
                    bool hasExited = true;
                    try { hasExited = mainProcess.HasExited; } catch { }

                    if (!hasExited)
                    {
                        mainProcess.Kill(true);
                        mainProcess.WaitForExit(2000);
                    }
                }
                catch { /* 忽略所有杀进程错误，比如拒绝访问或进程已结束 */ }
                finally
                {
                    try { mainProcess.Dispose(); } catch { }
                }
            }

            var allNames = new List<string>();
            if (!string.IsNullOrWhiteSpace(task.ProcessNames))
                allNames.AddRange(task.ProcessNames.Split(',', StringSplitOptions.RemoveEmptyEntries));
            if (!string.IsNullOrWhiteSpace(task.ExtraProcessNames))
                allNames.AddRange(task.ExtraProcessNames.Split(',', StringSplitOptions.RemoveEmptyEntries));

            foreach (var name in allNames)
            {
                string clean = name.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrEmpty(clean)) continue;

                try
                {
                    foreach (var p in Process.GetProcessesByName(clean))
                    {
                        try
                        {
                            bool hasExited = true;
                            try { hasExited = p.HasExited; } catch { }

                            if (!hasExited)
                            {
                                p.Kill(true);
                                p.WaitForExit(1000);
                            }
                        }
                        catch { }
                        finally { try { p.Dispose(); } catch { } }
                    }
                }
                catch { }
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