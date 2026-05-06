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

                try
                {
                    await RunSingleTask(task);
                }
                catch (Exception ex)
                {
                    Log("严重错误", $"任务 [{task.Name}] 发生系统级崩溃: {ex.Message}");
                    task.Status = "运行崩溃";
                    KillRelatedProcesses(task, null);
                }

                if (!_stopRequested && i < total - 1)
                {
                    Log("系统", "任务间隔：清理内存，等待 5 秒...");
                    GC.Collect();
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
                // 1. 先启动游戏/模拟器
                if (!string.IsNullOrWhiteSpace(task.ExtraStartPath) && File.Exists(task.ExtraStartPath))
                {
                    Log("系统", $"启动游戏/模拟器: {task.ExtraStartPath}");
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = task.ExtraStartPath, Arguments = task.ExtraStartArguments, UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(task.ExtraStartPath) ?? "" });

                        if (!string.IsNullOrWhiteSpace(task.WindowTitle))
                        {
                            Log("任务", $"严格等待游戏/模拟器窗口: [{task.WindowTitle}]");
                            if (!await WaitForWindow(task.WindowTitle, task.RecognitionTimeout))
                            {
                                Log("错误", "游戏/模拟器加载超时！为防止脚本空跑点错，中止当前任务！");
                                task.Status = "启动失败";
                                return; // 直接退出
                            }
                            Log("任务", "游戏/模拟器窗口加载成功！等待额外 5 秒确保渲染完毕...");
                            await Task.Delay(5000);
                        }
                    }
                    catch (Exception ex) { Log("警告", $"游戏启动失败: {ex.Message}"); return; }
                }

                // 2. 后启动脚本 (主程序)
                if (!string.IsNullOrWhiteSpace(task.Path) && File.Exists(task.Path))
                {
                    Log("系统", $"启动脚本: {task.Path}");
                    try
                    {
                        mainProcess = Process.Start(new ProcessStartInfo
                        {
                            FileName = task.Path,
                            Arguments = task.Arguments,
                            UseShellExecute = true,
                            WorkingDirectory = Path.GetDirectoryName(task.Path) ?? ""
                        });

                        if (mainProcess == null && !string.IsNullOrWhiteSpace(task.ProcessNames))
                        {
                            string procName = task.ProcessNames.Split(',')[0].Trim().Replace(".exe", "");
                            var procs = Process.GetProcessesByName(procName);
                            if (procs.Length > 0)
                            {
                                mainProcess = procs.OrderByDescending(p => {
                                    try { return p.StartTime; } catch { return DateTime.MinValue; }
                                }).FirstOrDefault();
                            }
                        }
                    }
                    catch (Exception ex) { Log("错误", $"脚本启动异常: {ex.Message}"); return; }
                }

                // 3. 核心时序控制：严格等待目标识别
                bool isTargetReady = false;

                if (!string.IsNullOrWhiteSpace(task.ScriptWindowTitle))
                {
                    Log("任务", $"严格等待脚本窗口加载: [{task.ScriptWindowTitle}]");
                    isTargetReady = await WaitForWindow(task.ScriptWindowTitle, task.RecognitionTimeout);
                }
                else if (!string.IsNullOrWhiteSpace(task.ProcessNames) || mainProcess != null)
                {
                    Log("任务", "未配置窗口，严格等待脚本进程加载...");
                    for (int i = 0; i < task.RecognitionTimeout; i++)
                    {
                        if (_stopRequested) return;
                        var alive = GetAliveProcesses(task, mainProcess);
                        if (alive.Count > 0) { isTargetReady = true; break; }
                        await Task.Delay(1000);
                    }
                }
                else { isTargetReady = true; }

                if (!isTargetReady)
                {
                    Log("错误", $"在 {task.RecognitionTimeout} 秒内未识别到目标脚本，任务中止");
                    task.Status = "启动失败";
                    KillRelatedProcesses(task, mainProcess);
                    return;
                }

                Log("任务", "目标识别成功，等待 3 秒程序稳定...");
                await Task.Delay(3000);

                // 执行宏录制动作
                if (task.Actions != null && task.Actions.Count > 0)
                {
                    Log("操作", $"开始执行宏操作，共 {task.Actions.Count} 步...");

                    IntPtr targetHwnd = IntPtr.Zero;
                    if (!string.IsNullOrWhiteSpace(task.ScriptWindowTitle))
                    {
                        targetHwnd = NativeMethods.FindWindowFuzzy(task.ScriptWindowTitle);
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


                // 任务监控阶段：双模监控
                task.Status = "运行中";
                Log("任务", $"开始监控运行，时长: {task.RunTime} 分钟");
                DateTime endTime = DateTime.Now.AddMinutes(task.RunTime);
                bool hasTakenScreenshot = false;

                if (!string.IsNullOrWhiteSpace(task.ScriptWindowTitle))
                {
                    // --- 模式一：窗口监控 ---
                    int missingCounter = 0;
                    string title = task.ScriptWindowTitle;
                    Log("监控", "开始窗口存活监控...");

                    while (DateTime.Now < endTime && !_stopRequested)
                    {
                        IntPtr hwnd = NativeMethods.FindWindowFuzzy(title);
                        if (hwnd == IntPtr.Zero)
                        {
                            missingCounter++;
                            if (missingCounter >= 5)
                            {
                                Log("监控", "脚本窗口关闭，任务结束");
                                break;
                            }
                        }
                        else missingCounter = 0;

                        if (!hasTakenScreenshot && (endTime - DateTime.Now).TotalSeconds <= 60)
                        {
                            Log("任务", "任务进入最后一分钟，执行自动截图...");
                            TakeScreenshot(task);
                            hasTakenScreenshot = true;
                        }
                        await Task.Delay(1000);
                    }
                }
                else
                {
                    // --- 模式二：进程名监控 ---
                    int missingCounter = 0;
                    Log("监控", "开始进程存活监控...");

                    while (DateTime.Now < endTime && !_stopRequested)
                    {
                        // 传入 mainProcess 获取最准确的存活状态
                        var alive = GetAliveProcesses(task, mainProcess);
                        bool currentlyAlive = alive.Count > 0;

                        if (!currentlyAlive)
                        {
                            missingCounter++;
                            if (missingCounter >= 5)
                            {
                                Log("监控", "所有目标进程均已退出，任务结束");
                                break;
                            }
                        }
                        else missingCounter = 0;

                        if (!hasTakenScreenshot && (endTime - DateTime.Now).TotalSeconds <= 60)
                        {
                            Log("任务", "任务进入最后一分钟，执行自动截图...");
                            TakeScreenshot(task);
                            hasTakenScreenshot = true;
                        }
                        await Task.Delay(1000);
                    }
                }

                // 保底截图
                if (!hasTakenScreenshot && !_stopRequested)
                {
                    Log("任务", "执行最终检查截图...");
                    TakeScreenshot(task);
                }

                task.Status = _stopRequested ? "手动停止" : "已完成";
                KillRelatedProcesses(task, null); 
                await SendBark(task.Name, task.Status);

                // 5. 结束收尾
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
                IntPtr hwnd = NativeMethods.FindWindowFuzzy(title);
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
                            // 【关键修复1】强制刷新进程对象，获取系统最底层的实时状态
                            p.Refresh();

                            if (!p.HasExited)
                            {
                                list.Add(p);
                            }
                        }
                        catch
                        {

                        }
                    }
                }
                catch { }
            }

            if (list.Count == 0 && mainProcess != null)
            {
                try
                {
                    mainProcess.Refresh();
                    if (!mainProcess.HasExited) list.Add(mainProcess);
                }
                catch { }
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