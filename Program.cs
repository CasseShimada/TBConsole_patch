using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TourBoxConsolePatch
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "TourBoxConsolePatch.SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("TourBox Console Patch 已经在运行。", "TourBox Console Patch",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var config = AppConfig.Load();
                Logger.Init(config.LogFilePath);
                Logger.Write("TourBox Console Patch started.");
                Application.Run(new TrayAppContext(config));
            }
        }
    }

    internal sealed class TrayAppContext : ApplicationContext
    {
        private readonly AppConfig _config;
        private readonly MonitorWorker _worker;
        private readonly NotifyIcon _notifyIcon;

        public TrayAppContext(AppConfig config)
        {
            _config = config;
            _worker = new MonitorWorker(config);

            var menu = new ContextMenuStrip();
            menu.Items.Add("立即重启 TourBox Console", null, delegate { _worker.RestartTourBox("manual tray action"); });
            menu.Items.Add("启动 TourBox Console", null, delegate { _worker.StartTourBox("manual tray action"); });
            menu.Items.Add("关闭 TourBox Console", null, delegate { _worker.StopTourBox("manual tray action", false); });
            menu.Items.Add("打开配置文件", null, delegate { OpenPath(_config.ConfigFilePath); });
            menu.Items.Add("打开日志", null, delegate { OpenPath(_config.LogFilePath); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitThread(); });

            _notifyIcon = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
                Text = "TourBox Console Patch",
                ContextMenuStrip = menu,
                Visible = true
            };

            _notifyIcon.DoubleClick += delegate { OpenPath(_config.ConfigFilePath); };
            _notifyIcon.ShowBalloonTip(2500, "TourBox Console Patch",
                "正在监控 Clip Studio Paint，并会按配置暂停/恢复 TourBox Console。", ToolTipIcon.Info);

            _worker.Start();
        }

        protected override void ExitThreadCore()
        {
            _worker.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            Logger.Write("TourBox Console Patch stopped.");
            base.ExitThreadCore();
        }

        private static void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "无法打开", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    internal sealed class MonitorWorker : IDisposable
    {
        private readonly AppConfig _config;
        private readonly System.Threading.Timer _timer;
        private DateTime? _focusLostSinceUtc;
        private DateTime? _clipStudioForegroundSinceUtc;
        private bool _clipStudioSessionActive;
        private bool _clipStudioWasForeground;
        private bool _tourBoxPausedByPatch;
        private bool _disposed;
        private int _tickRunning;

        public MonitorWorker(AppConfig config)
        {
            _config = config;
            _timer = new System.Threading.Timer(Tick, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start()
        {
            _timer.Change(1000, Math.Max(500, _config.PollIntervalMs));
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
        }

        private void Tick(object state)
        {
            if (_disposed)
            {
                return;
            }

            if (Interlocked.Exchange(ref _tickRunning, 1) == 1)
            {
                return;
            }

            try
            {
                if (!File.Exists(_config.ClipStudioPath))
                {
                    Logger.Write("Clip Studio Paint path does not exist: " + _config.ClipStudioPath);
                    return;
                }

                if (!IsProcessRunningFromPath(_config.ClipStudioPath))
                {
                    ResetClipStudioSession();
                    return;
                }

                var foregroundPath = NativeMethods.GetForegroundProcessPath();
                var clipStudioIsForeground = PathsEqual(foregroundPath, _config.ClipStudioPath);

                if (clipStudioIsForeground)
                {
                    if (!_clipStudioWasForeground)
                    {
                        _clipStudioWasForeground = true;
                        _clipStudioForegroundSinceUtc = DateTime.UtcNow;
                        StartTourBox("Clip Studio Paint became foreground");
                    }

                    _clipStudioSessionActive = true;
                    _focusLostSinceUtc = null;

                    var idleSeconds = NativeMethods.GetSystemIdleTime().TotalSeconds;
                    var foregroundSeconds = _clipStudioForegroundSinceUtc.HasValue
                        ? (DateTime.UtcNow - _clipStudioForegroundSinceUtc.Value).TotalSeconds
                        : 0;

                    if (_tourBoxPausedByPatch)
                    {
                        if (idleSeconds < Math.Max(1, _config.IdleSeconds))
                        {
                            StartTourBox("Clip Studio Paint activity resumed");
                        }

                        return;
                    }

                    if (_config.StopOnIdle &&
                        foregroundSeconds >= Math.Max(1, _config.ForegroundGraceSeconds) &&
                        idleSeconds >= Math.Max(1, _config.IdleSeconds))
                    {
                        StopTourBox("Clip Studio Paint idle for " + (int)idleSeconds + " seconds", true);
                    }

                    return;
                }

                _clipStudioWasForeground = false;
                _clipStudioForegroundSinceUtc = null;

                if (!_tourBoxPausedByPatch && _config.StopOnFocusLost && _clipStudioSessionActive)
                {
                    if (!_focusLostSinceUtc.HasValue)
                    {
                        _focusLostSinceUtc = DateTime.UtcNow;
                    }

                    var lostSeconds = (DateTime.UtcNow - _focusLostSinceUtc.Value).TotalSeconds;
                    if (lostSeconds >= Math.Max(0, _config.FocusLostDelaySeconds))
                    {
                        StopTourBox("Clip Studio Paint lost focus for " + (int)lostSeconds + " seconds", true);
                        _focusLostSinceUtc = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Write("Monitor error: " + ex);
            }
            finally
            {
                Interlocked.Exchange(ref _tickRunning, 0);
            }
        }

        public void RestartTourBox(string reason)
        {
            try
            {
                StopTourBox(reason, false);
                StartTourBox(reason);
            }
            catch (Exception ex)
            {
                Logger.Write("Restart failed: " + ex);
            }
        }

        public bool StopTourBox(string reason, bool resumeOnActivity)
        {
            try
            {
                var stoppedAny = false;
                Logger.Write("Stopping TourBox Console. Reason: " + reason);

                foreach (var process in FindProcessesByPath(_config.TourBoxPath))
                {
                    stoppedAny = true;
                    StopProcess(process);
                }

                if (stoppedAny && resumeOnActivity)
                {
                    _tourBoxPausedByPatch = true;
                    Logger.Write("TourBox Console paused until Clip Studio Paint activity resumes.");
                }
                else if (!stoppedAny)
                {
                    Logger.Write("TourBox Console was not running.");
                }

                return stoppedAny;
            }
            catch (Exception ex)
            {
                Logger.Write("Stop failed: " + ex);
                return false;
            }
        }

        public void StartTourBox(string reason)
        {
            try
            {
                if (!File.Exists(_config.TourBoxPath))
                {
                    Logger.Write("TourBox Console path does not exist: " + _config.TourBoxPath);
                    return;
                }

                if (IsProcessRunningFromPath(_config.TourBoxPath))
                {
                    _tourBoxPausedByPatch = false;
                    Logger.Write("TourBox Console already running. Reason: " + reason);
                    return;
                }

                Logger.Write("Starting TourBox Console. Reason: " + reason);

                var startedProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = _config.TourBoxPath,
                    WorkingDirectory = Path.GetDirectoryName(_config.TourBoxPath),
                    UseShellExecute = true
                });

                _tourBoxPausedByPatch = false;
                Logger.Write("TourBox Console launched.");

                if (_config.MinimizeTourBoxAfterStart)
                {
                    MinimizeTourBoxWhenReady(startedProcess);
                }
            }
            catch (Exception ex)
            {
                Logger.Write("Start failed: " + ex);
            }
        }

        private void MinimizeTourBoxWhenReady(Process startedProcess)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    for (var attempt = 0; attempt < 50; attempt++)
                    {
                        var process = ResolveTourBoxProcess(startedProcess);
                        if (process != null)
                        {
                            using (process)
                            {
                                process.Refresh();
                                if (process.MainWindowHandle != IntPtr.Zero)
                                {
                                    NativeMethods.MinimizeWindow(process.MainWindowHandle);
                                    Logger.Write("TourBox Console minimized after start.");
                                    return;
                                }
                            }
                        }

                        Thread.Sleep(200);
                    }

                    Logger.Write("TourBox Console window was not found for minimization.");
                }
                catch (Exception ex)
                {
                    Logger.Write("Minimize failed: " + ex.Message);
                }
            });
        }

        private Process ResolveTourBoxProcess(Process startedProcess)
        {
            if (startedProcess != null)
            {
                try
                {
                    if (!startedProcess.HasExited)
                    {
                        return Process.GetProcessById(startedProcess.Id);
                    }
                }
                catch
                {
                }
            }

            foreach (var process in FindProcessesByPath(_config.TourBoxPath))
            {
                return process;
            }

            return null;
        }

        private void ResetClipStudioSession()
        {
            _clipStudioSessionActive = false;
            _clipStudioWasForeground = false;
            _focusLostSinceUtc = null;
            _clipStudioForegroundSinceUtc = null;
        }

        private static bool IsProcessRunningFromPath(string expectedPath)
        {
            foreach (var process in FindProcessesByPath(expectedPath))
            {
                process.Dispose();
                return true;
            }

            return false;
        }

        private static IEnumerable<Process> FindProcessesByPath(string expectedPath)
        {
            var expectedName = Path.GetFileNameWithoutExtension(expectedPath);
            foreach (var process in Process.GetProcessesByName(expectedName))
            {
                var matches = false;
                try
                {
                    matches = PathsEqual(process.MainModule.FileName, expectedPath);
                }
                catch
                {
                    matches = string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase);
                }

                if (matches)
                {
                    yield return process;
                }
                else
                {
                    process.Dispose();
                }
            }
        }

        private static void StopProcess(Process process)
        {
            using (process)
            {
                try
                {
                    Logger.Write("Stopping process " + process.Id + " (" + process.ProcessName + ").");
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        process.CloseMainWindow();
                        if (process.WaitForExit(3000))
                        {
                            return;
                        }
                    }

                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Write("Failed to stop process " + SafeProcessId(process) + ": " + ex.Message);
                }
            }
        }

        private static int SafeProcessId(Process process)
        {
            try
            {
                return process.Id;
            }
            catch
            {
                return -1;
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(Path.GetFullPath(left).TrimEnd('\\'),
                Path.GetFullPath(right).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class AppConfig
    {
        public string TourBoxPath { get; private set; }
        public string ClipStudioPath { get; private set; }
        public int IdleSeconds { get; private set; }
        public int ForegroundGraceSeconds { get; private set; }
        public int FocusLostDelaySeconds { get; private set; }
        public int PollIntervalMs { get; private set; }
        public bool StopOnIdle { get; private set; }
        public bool StopOnFocusLost { get; private set; }
        public bool MinimizeTourBoxAfterStart { get; private set; }
        public string ConfigFilePath { get; private set; }
        public string LogFilePath { get; private set; }

        public static AppConfig Load()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TourBoxConsolePatch");
            Directory.CreateDirectory(appData);

            var startupConfig = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TourBoxConsolePatch.ini");
            var configPath = CanWriteToDirectory(AppDomain.CurrentDomain.BaseDirectory)
                ? startupConfig
                : Path.Combine(appData, "TourBoxConsolePatch.ini");

            var config = Defaults(configPath, Path.Combine(appData, "patch.log"));

            if (!File.Exists(configPath))
            {
                File.WriteAllText(configPath, config.ToIniText(), Encoding.UTF8);
                return config;
            }

            foreach (var rawLine in File.ReadAllLines(configPath, Encoding.UTF8))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                {
                    continue;
                }

                var equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, equalsIndex).Trim();
                var value = line.Substring(equalsIndex + 1).Trim();
                config.Apply(key, value);
            }

            return config;
        }

        private static AppConfig Defaults(string configPath, string logPath)
        {
            return new AppConfig
            {
                TourBoxPath = @"C:\Program Files\TourBox Console\TourBox Console.exe",
                ClipStudioPath = @"C:\Program Files\CELSYS\CLIP STUDIO 1.5\CLIP STUDIO PAINT\CLIPStudioPaint.exe",
                IdleSeconds = 60,
                ForegroundGraceSeconds = 2,
                FocusLostDelaySeconds = 5,
                PollIntervalMs = 1000,
                StopOnIdle = true,
                StopOnFocusLost = true,
                MinimizeTourBoxAfterStart = true,
                ConfigFilePath = configPath,
                LogFilePath = logPath
            };
        }

        private void Apply(string key, string value)
        {
            if (key.Equals("TourBoxPath", StringComparison.OrdinalIgnoreCase))
            {
                TourBoxPath = value;
            }
            else if (key.Equals("ClipStudioPath", StringComparison.OrdinalIgnoreCase))
            {
                ClipStudioPath = value;
            }
            else if (key.Equals("IdleSeconds", StringComparison.OrdinalIgnoreCase))
            {
                IdleSeconds = ParseInt(value, IdleSeconds);
            }
            else if (key.Equals("ForegroundGraceSeconds", StringComparison.OrdinalIgnoreCase))
            {
                ForegroundGraceSeconds = ParseInt(value, ForegroundGraceSeconds);
            }
            else if (key.Equals("FocusLostDelaySeconds", StringComparison.OrdinalIgnoreCase))
            {
                FocusLostDelaySeconds = ParseInt(value, FocusLostDelaySeconds);
            }
            else if (key.Equals("PollIntervalMs", StringComparison.OrdinalIgnoreCase))
            {
                PollIntervalMs = ParseInt(value, PollIntervalMs);
            }
            else if (key.Equals("StopOnIdle", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("RestartOnIdle", StringComparison.OrdinalIgnoreCase))
            {
                StopOnIdle = ParseBool(value, StopOnIdle);
            }
            else if (key.Equals("StopOnFocusLost", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("RestartOnFocusLost", StringComparison.OrdinalIgnoreCase))
            {
                StopOnFocusLost = ParseBool(value, StopOnFocusLost);
            }
            else if (key.Equals("MinimizeTourBoxAfterStart", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("MinimizeTourBoxAfterRestart", StringComparison.OrdinalIgnoreCase))
            {
                MinimizeTourBoxAfterStart = ParseBool(value, MinimizeTourBoxAfterStart);
            }
            else if (key.Equals("LogFilePath", StringComparison.OrdinalIgnoreCase))
            {
                LogFilePath = Environment.ExpandEnvironmentVariables(value);
            }
        }

        private string ToIniText()
        {
            return
                "# TourBox Console Patch configuration\r\n" +
                "# Changes take effect after restarting this patch program.\r\n" +
                "TourBoxPath=" + TourBoxPath + "\r\n" +
                "ClipStudioPath=" + ClipStudioPath + "\r\n" +
                "IdleSeconds=" + IdleSeconds + "\r\n" +
                "ForegroundGraceSeconds=" + ForegroundGraceSeconds + "\r\n" +
                "FocusLostDelaySeconds=" + FocusLostDelaySeconds + "\r\n" +
                "PollIntervalMs=" + PollIntervalMs + "\r\n" +
                "StopOnIdle=" + StopOnIdle + "\r\n" +
                "StopOnFocusLost=" + StopOnFocusLost + "\r\n" +
                "MinimizeTourBoxAfterStart=" + MinimizeTourBoxAfterStart + "\r\n" +
                "LogFilePath=" + LogFilePath + "\r\n";
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, out parsed) ? parsed : fallback;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            bool parsed;
            if (bool.TryParse(value, out parsed))
            {
                return parsed;
            }

            if (value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value == "0" || value.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return fallback;
        }

        private static bool CanWriteToDirectory(string directory)
        {
            try
            {
                var probe = Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class Logger
    {
        private static readonly object SyncRoot = new object();
        private static string _logFilePath;

        public static void Init(string logFilePath)
        {
            _logFilePath = logFilePath;
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public static void Write(string message)
        {
            try
            {
                lock (SyncRoot)
                {
                    File.AppendAllText(_logFilePath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch
            {
            }
        }
    }

    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LastInputInfo
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LastInputInfo plii);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SwMinimize = 6;

        public static void MinimizeWindow(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, SwMinimize);
            }
        }

        public static string GetForegroundProcessPath()
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            uint processId;
            GetWindowThreadProcessId(handle, out processId);
            if (processId == 0)
            {
                return null;
            }

            try
            {
                using (var process = Process.GetProcessById((int)processId))
                {
                    return process.MainModule.FileName;
                }
            }
            catch
            {
                return null;
            }
        }

        public static TimeSpan GetSystemIdleTime()
        {
            var info = new LastInputInfo();
            info.cbSize = (uint)Marshal.SizeOf(typeof(LastInputInfo));

            if (!GetLastInputInfo(ref info))
            {
                return TimeSpan.Zero;
            }

            var tickCount = unchecked((uint)Environment.TickCount);
            var idleMilliseconds = unchecked(tickCount - info.dwTime);
            return TimeSpan.FromMilliseconds(idleMilliseconds);
        }
    }
}
