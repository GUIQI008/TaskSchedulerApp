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

                #region 启动主程序并捕获进程
                var (launchSuccess, launchedProcess) = await AttemptLaunch(task);
                mainProcess = launchedProcess;

                if (!launchSuccess)
                {
                    if (_stopRequested)
                    {
                        Log("系统", $"{task.Name} 启动过程中被手动停止");
                        task.Status = "手动停止";
                        KillRelatedProcesses(task, mainProcess);
                        return;
                    }

                    Log("错误", $"{task.Name} 启动失败（窗口/进程未检测到），跳过任务");
                    KillRelatedProcesses(task, mainProcess);
                    task.Status = "启动失败";
                    return;
                }

                Log("任务", mainProcess != null
                    ? $"主程序启动成功，PID: {mainProcess.Id}，进程名: {mainProcess.ProcessName}"
                    : "主程序启动成功（通过窗口检测确认）");
                #endregion

                Log("任务", "等待 3 秒窗口稳定...");
                await Task.Delay(3000);

                #region 宏动作回放
                // 原代码段（约第 70-95 行）替换为：
                if (task.Actions != null && task.Actions.Count > 0)
                {
                    Log("操作", $"开始执行宏操作，共 {task.Actions.Count} 步...");

                    IntPtr targetHwnd = IntPtr.Zero;
                    if (!string.IsNullOrWhiteSpace(task.MacroWindowTitle))
                    {
                        targetHwnd = NativeMethods.FindWindow(null, task.MacroWindowTitle);
                        if (targetHwnd != IntPtr.Zero)
                        {
                            Log("系统", "已将脚本窗口置顶");
                            if (NativeMethods.IsIconic(targetHwnd))
                                NativeMethods.ShowWindow(targetHwnd, NativeMethods.SW_RESTORE);
                            NativeMethods.SetForegroundWindow(targetHwnd);
                            NativeMethods.SetWindowPos(targetHwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
                        }
                        else
                        {
                            Log("警告", $"未找到脚本窗口 [{task.MacroWindowTitle}]，宏操作可能失效。");
                        }
                    }

                    foreach (var action in task.Actions)
                    {
                        if (_stopRequested) break;

                        if (action.DelayBefore > 0) await Task.Delay(action.DelayBefore);

                        // 转换坐标：如果提供了目标窗口，将客户区坐标转为屏幕坐标
                        int screenX = action.X, screenY = action.Y;
                        if (targetHwnd != IntPtr.Zero)
                        {
                            var pt = new System.Drawing.Point(action.X, action.Y);
                            NativeMethods.ClientToScreen(targetHwnd, ref pt);
                            screenX = pt.X;
                            screenY = pt.Y;
                        }

                        // 执行动作时使用屏幕坐标
                        switch (action.ActionType)
                        {

                            case MacroActionType.MouseMove:
                                NativeMethods.SetCursorPos(screenX, screenY);
                                break;
                            case MacroActionType.MouseLeftUp:
                                NativeMethods.SetCursorPos(screenX, screenY);
                                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                                break;
                            case MacroActionType.MouseRightUp:
                                NativeMethods.SetCursorPos(screenX, screenY);
                                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                                break;
                            default:
                                InputSimulator.ExecuteAction(action);
                                break;
                            case MacroActionType.MouseLeftDown:
                                NativeMethods.SetCursorPos(screenX, screenY);
                                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                                await Task.Delay(50);
                                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                                break;
                            case MacroActionType.MouseRightDown:
                                NativeMethods.SetCursorPos(screenX, screenY);
                                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                                await Task.Delay(50);
                                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                                break;
                        }
                    }

                    if (targetHwnd != IntPtr.Zero)
                    {
                        Log("系统", "已恢复脚本窗口正常层级");
                        NativeMethods.SetWindowPos(targetHwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
                            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
                    }

                    Log("操作", "宏操作执行完毕！");
                }
                #endregion

                task.Status = "运行中";
                Log("任务", $"开始监控运行，时长: {task.RunTime} 分钟");
                await SendBark(task.Name, "运行中");

                DateTime endTime = DateTime.Now.AddMinutes(task.RunTime);
                int missingCounter = 0;
                bool hasTakenScreenshot = false;

                #region 主监控循环
                while (DateTime.Now < endTime && !_stopRequested)
                {
                    #region 进程存活检测（固定5秒）
                    var aliveProcesses = GetAliveProcesses(task);
                    bool currentlyAlive = aliveProcesses.Count > 0;

                    if (currentlyAlive != lastAliveState)
                    {
                        Log("监控", currentlyAlive ? $"目标进程恢复，PID: {string.Join(", ", aliveProcesses.Select(p => p.Id))}" : "目标进程丢失（连续5秒结束任务）");
                        lastAliveState = currentlyAlive;
                    }

                    if (!currentlyAlive)
                    {
                        missingCounter++;
                        if (missingCounter >= 5)
                        {
                            Log("监控", $"{task.Name} 主进程连续丢失5秒，任务结束");

                            KillRelatedProcesses(task, mainProcess);
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

                    if (!hasTakenScreenshot && (endTime - DateTime.Now).TotalSeconds <= 60)
                    {
                        Log("任务", "任务进入最后一分钟，执行自动截图...");
                        TakeScreenshot(task);
                        hasTakenScreenshot = true;
                    }

                    await Task.Delay(1000);
                }
                #endregion

                #region 结束处理
                if (_stopRequested)
                {
                    task.Status = "手动停止";
                    KillRelatedProcesses(task, mainProcess);
                    await SendBark(task.Name, "手动停止");
                }
                else
                {
                    if (!hasTakenScreenshot)
                    {
                        Log("任务", "任务即将结束，执行最终截图...");
                        TakeScreenshot(task);
                    }

                    task.Status = "已完成";

                    KillRelatedProcesses(task, mainProcess);
                    await SendBark(task.Name, "运行结束（正常）");
                }
                #endregion
            }
            finally
            {
                KillRelatedProcesses(task, mainProcess);
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

            Log("任务", $"开始窗口标题检测: \"{task.WindowTitle}\"，总时长 {task.RecognitionTimeout} 秒（需连续2次稳定）");

            int requiredStability = 2;
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

                    Log("监控", $"检测到目标窗口（连续 {consecutiveCount}/{requiredStability} 次）");

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
                    if (consecutiveCount > 0)
                        Log("监控", "窗口短暂消失，重置稳定计数");

                    consecutiveCount = 0;
                }

                await Task.Delay(1000);
            }

            Log("错误", $"窗口标题 \"{task.WindowTitle}\" 在 {task.RecognitionTimeout} 秒内未达到连续 {requiredStability} 次稳定");
            Log("提示", "建议检查标题精确匹配（大小写、空格）或增大超时时间");
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