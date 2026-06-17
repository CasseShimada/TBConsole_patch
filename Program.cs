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
            menu.Items.Add("关闭 TourBox Console", null, delegate { _worker.StopTourBox("manual tray action"); });
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

            _notifyIcon.DoubleClick += delegate { _worker.RestartTourBox("manual tray double click"); };
            _notifyIcon.ShowBalloonTip(2500, "TourBox Console Patch",
                "双击托盘图标即可重启 TourBox Console。", ToolTipIcon.Info);
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

        public MonitorWorker(AppConfig config)
        {
            _config = config;
        }

        public void Start()
        {
        }

        public void Dispose()
        {
        }

        public void RestartTourBox(string reason)
        {
            try
            {
                StopTourBox(reason);
                StartTourBox(reason);
            }
            catch (Exception ex)
            {
                Logger.Write("Restart failed: " + ex);
            }
        }

        public bool StopTourBox(string reason)
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

                if (!stoppedAny)
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
                    Logger.Write("TourBox Console already running. Reason: " + reason);
                    if (_config.HideTourBoxWindowAfterStart)
                    {
                        HideTourBoxWindowWhenReady(null);
                    }
                    return;
                }

                Logger.Write("Starting TourBox Console. Reason: " + reason);

                var startedProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = _config.TourBoxPath,
                    WorkingDirectory = Path.GetDirectoryName(_config.TourBoxPath),
                    UseShellExecute = true
                });

                Logger.Write("TourBox Console launched.");

                if (_config.HideTourBoxWindowAfterStart)
                {
                    HideTourBoxWindowWhenReady(startedProcess);
                }
            }
            catch (Exception ex)
            {
                Logger.Write("Start failed: " + ex);
            }
        }

        private void HideTourBoxWindowWhenReady(Process startedProcess)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    for (var attempt = 0; attempt < 100; attempt++)
                    {
                        var process = ResolveTourBoxProcess(startedProcess);
                        if (process != null)
                        {
                            using (process)
                            {
                                process.Refresh();
                                var hiddenCount = NativeMethods.HideVisibleTopLevelWindowsForProcess(process.Id);
                                if (hiddenCount > 0)
                                {
                                    Logger.Write("TourBox Console hidden window count after start: " + hiddenCount + ".");
                                    return;
                                }
                            }
                        }

                        Thread.Sleep(200);
                    }

                    Logger.Write("TourBox Console window was not found to hide.");
                }
                catch (Exception ex)
                {
                    Logger.Write("Hide window failed: " + ex.Message);
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
        public bool HideTourBoxWindowAfterStart { get; private set; }
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
                HideTourBoxWindowAfterStart = true,
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
            else if (key.Equals("HideTourBoxWindowAfterStart", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("MinimizeTourBoxAfterStart", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("MinimizeTourBoxAfterRestart", StringComparison.OrdinalIgnoreCase))
            {
                HideTourBoxWindowAfterStart = ParseBool(value, HideTourBoxWindowAfterStart);
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
                "HideTourBoxWindowAfterStart=" + HideTourBoxWindowAfterStart + "\r\n" +
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
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private const int SwHide = 0;

        public static int HideVisibleTopLevelWindowsForProcess(int processId)
        {
            var hiddenCount = 0;
            EnumWindows(delegate(IntPtr handle, IntPtr parameter)
            {
                uint windowProcessId;
                GetWindowThreadProcessId(handle, out windowProcessId);

                if (windowProcessId == processId && IsWindowVisible(handle))
                {
                    ShowWindow(handle, SwHide);
                    hiddenCount++;
                }

                return true;
            }, IntPtr.Zero);

            return hiddenCount;
        }
    }
}
