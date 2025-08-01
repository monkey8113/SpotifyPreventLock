using System;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace SpotifyPreventLock
{
    public class AppSettings
    {
        public int CheckInterval { get; set; } = 2000; // Default 2000ms (2 seconds)
    }

    public class PreventLockApp : ApplicationContext
    {
        [DllImport("kernel32.dll")]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        [Flags]
        private enum ExecutionState : uint
        {
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
        }

        private readonly NotifyIcon trayIcon;
        private volatile bool isRunning;
        private readonly AppSettings settings;
        private readonly string settingsPath;
        private readonly string settingsDirectory;
        private readonly string appVersion;
        private readonly Font versionFont;

        public PreventLockApp()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            appVersion = $"v{version.Major}.{version.Minor}.{version.Build}";
            versionFont = new Font("Segoe UI", 8.25f, FontStyle.Italic);

            settingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SpotifyPreventLock");
            Directory.CreateDirectory(settingsDirectory);
            settingsPath = Path.Combine(settingsDirectory, "settings.json");

            settings = LoadSettings();
            isRunning = true;

            ValidateAndFixStartupPath();

            trayIcon = new NotifyIcon()
            {
                Icon = LoadTrayIcon(false),
                Text = $"Spotify Prevent Lock {appVersion}\nCheck Interval: {settings.CheckInterval}ms",
                Visible = true,
                ContextMenuStrip = CreateContextMenu()
            };

            new Thread(WorkerThreadMethod) { IsBackground = true }.Start();
        }

        private void ValidateAndFixStartupPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                
                if (key?.GetValue("SpotifyPreventLock") is string currentValue)
                {
                    string currentPath = currentValue.Trim('"');
                    string actualPath = Application.ExecutablePath;
                    
                    if (!currentPath.Equals(actualPath, StringComparison.OrdinalIgnoreCase))
                    {
                        key.SetValue("SpotifyPreventLock", $"\"{actualPath}\"");
                    }
                }
            }
            catch { /* Silent failure is acceptable */ }
        }

        private AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }

                var defaultSettings = new AppSettings();
                SaveSettings(defaultSettings);
                return defaultSettings;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading settings: {ex.Message}");
                return new AppSettings();
            }
        }

        private void SaveSettings(AppSettings? settingsToSave = null)
        {
            try
            {
                settingsToSave ??= settings;
                string json = JsonSerializer.Serialize(settingsToSave);
                File.WriteAllText(settingsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        private Icon LoadTrayIcon(bool isPlaying)
        {
            try
            {
                string iconName = isPlaying ? "SpotifyPreventLock.app.ico" : "SpotifyPreventLock.appoff.ico";
                using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(iconName);
                if (stream != null)
                {
                    return new Icon(stream);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading embedded icon: {ex.Message}");
            }

            return CreateCircleIcon(isPlaying ? Color.LimeGreen : Color.Gray);
        }

        private Icon CreateCircleIcon(Color color)
        {
            using var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using var brush = new SolidBrush(color);
                g.FillEllipse(brush, 0, 0, 15, 15);
            }
            var iconHandle = bmp.GetHicon();
            try
            {
                return Icon.FromHandle(iconHandle);
            }
            finally
            {
                DestroyIcon(iconHandle);
            }
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();

            var timerItem = new ToolStripMenuItem("Set Check Interval...");
            timerItem.Click += (s, e) => ShowTimerDialog();
            menu.Items.Add(timerItem);

            var startupItem = new ToolStripMenuItem("Start with Windows");
            startupItem.Click += (s, e) => ToggleStartup();
            UpdateStartupMenuItem(startupItem);
            menu.Items.Add(startupItem);

            var versionItem = new ToolStripMenuItem(appVersion)
            {
                Enabled = false,
                Font = versionFont
            };
            menu.Items.Add(versionItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => OnExit());

            return menu;
        }

        private void ToggleStartup()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

                if (key != null)
                {
                    if (IsInStartup())
                    {
                        key.DeleteValue("SpotifyPreventLock", false);
                    }
                    else
                    {
                        key.SetValue("SpotifyPreventLock", 
                            $"\"{Application.ExecutablePath}\"");
                    }

                    UpdateStartupMenuItem();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error toggling startup: {ex.Message}");
                MessageBox.Show("Failed to update startup settings. Please try again.",
                    "Startup Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void UpdateStartupMenuItem(ToolStripMenuItem? menuItem = null)
        {
            var item = menuItem ?? (trayIcon.ContextMenuStrip?.Items[1] as ToolStripMenuItem);
            if (item != null)
            {
                bool isEnabled = IsInStartup();
                item.Checked = isEnabled;
                item.Text = isEnabled ? "✓ Start with Windows" : "Start with Windows";
            }
        }

        private bool IsInStartup()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", false);
                return key?.GetValue("SpotifyPreventLock") != null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking startup: {ex.Message}");
                return false;
            }
        }

        private void ShowTimerDialog()
        {
            using var dialog = new Form()
            {
                Text = "Set Check Interval",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Width = 300,
                Height = 180,
                StartPosition = FormStartPosition.CenterScreen,
                ShowInTaskbar = false
            };

            var infoLabel = new Label()
            {
                Text = "Delay for checking Spotify activity (100ms = 0.1s, 1000ms = 1s, 2000ms = 2s)",
                Top = 20,
                Left = 20,
                Width = 260,
                Height = 40
            };

            var numericBox = new NumericUpDown()
            {
                Minimum = 100,
                Maximum = 100000,
                Value = settings.CheckInterval,
                Width = 80,
                Top = 70,
                Left = 110,
                Increment = 100
            };

            var label = new Label()
            {
                Text = "Time(ms):",
                Top = 73,
                Left = 40,
                Width = 60
            };

            var okButton = new Button()
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Top = 110,
                Left = 110,
                Width = 75
            };

            dialog.Controls.AddRange(new Control[] { infoLabel, label, numericBox, okButton });

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                settings.CheckInterval = (int)numericBox.Value;
                trayIcon.Text = $"Spotify Prevent Lock {appVersion}\nCheck Interval: {settings.CheckInterval}ms";
                SaveSettings();
            }
        }

        private void WorkerThreadMethod()
        {
            bool wasPlaying = false;

            while (isRunning)
            {
                bool isPlaying = IsSpotifyActive();

                if (isPlaying != wasPlaying)
                {
                    UpdateSystemState(isPlaying);
                    wasPlaying = isPlaying;
                }

                Thread.Sleep(settings.CheckInterval);
            }
        }

        private void UpdateSystemState(bool isPlaying)
        {
            SetThreadExecutionState(isPlaying
                ? ExecutionState.ES_DISPLAY_REQUIRED | ExecutionState.ES_CONTINUOUS
                : ExecutionState.ES_CONTINUOUS);

            trayIcon.Icon = LoadTrayIcon(isPlaying);
        }

        private bool IsSpotifyActive()
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName("Spotify"))
                {
                    if (proc != null && !string.IsNullOrEmpty(proc.MainWindowTitle) &&
                        !proc.MainWindowTitle.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking Spotify: {ex.Message}");
            }
            return false;
        }

        private void OnExit()
        {
            isRunning = false;
            trayIcon.Visible = false;
            trayIcon.Dispose();
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                versionFont?.Dispose();
                trayIcon?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    static class Program
    {
        private static Mutex? _mutex;
        private const string AppName = "Spotify Prevent Lock";

        [STAThread]
        static void Main()
        {
            const string mutexName = "Global\\SpotifyPreventLock";
            bool createdNew;
            _mutex = new Mutex(true, mutexName, out createdNew);

            if (!createdNew)
            {
                ShowRunningInstanceWarning();
                return;
            }

            try
            {
                var versionCheck = CheckRunningVersions();
                if (versionCheck != VersionCheckResult.Continue)
                {
                    if (versionCheck == VersionCheckResult.Restart)
                    {
                        Thread.Sleep(500); // Brief pause before restart
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = Application.ExecutablePath,
                            UseShellExecute = true
                        });
                    }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new PreventLockApp());
            }
            finally
            {
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
            }
        }

        private enum VersionCheckResult
        {
            Continue,
            Exit,
            Restart
        }

        private static void ShowRunningInstanceWarning()
        {
            MessageBox.Show(
                $"{AppName} is already running.\n\n" +
                "Please exit old instance and rerun new version to upgrade.",
                "Application Running",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static VersionCheckResult CheckRunningVersions()
        {
            var currentProcess = Process.GetCurrentProcess();
            var currentVersion = GetCurrentVersion();

            foreach (var process in Process.GetProcessesByName(currentProcess.ProcessName))
            {
                try
                {
                    if (process.Id == currentProcess.Id) continue;

                    var runningVersion = GetProcessVersion(process);
                    if (runningVersion == null) continue;

                    int comparison = runningVersion.CompareTo(currentVersion);

                    if (comparison > 0) // Newer version running
                    {
                        MessageBox.Show(
                            $"A newer version (v{runningVersion}) is already running!\n\n" +
                            $"Please close this version (v{currentVersion}) and use the newer one.",
                            "New Version Detected",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return VersionCheckResult.Exit;
                    }
                    else if (comparison < 0) // Older version running
                    {
                        return HandleOlderVersion(process, currentVersion, runningVersion);
                    }
                    else // Same version running
                    {
                        return VersionCheckResult.Exit;
                    }
                }
                catch { /* Ignore inaccessible processes */ }
            }
            return VersionCheckResult.Continue;
        }

        private static Version GetCurrentVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        }

        private static Version? GetProcessVersion(Process process)
        {
            try
            {
                string? path = process.MainModule?.FileName;
                if (string.IsNullOrEmpty(path)) return null;

                var versionInfo = FileVersionInfo.GetVersionInfo(path);
                return string.IsNullOrEmpty(versionInfo.FileVersion) 
                    ? null 
                    : new Version(versionInfo.FileVersion);
            }
            catch
            {
                return null;
            }
        }

        private static VersionCheckResult HandleOlderVersion(Process process, Version currentVersion, Version runningVersion)
        {
            var result = MessageBox.Show(
                $"An older version (v{runningVersion}) is running.\n\n" +
                $"Current version: v{currentVersion}\n\n" +
                "Would you like to upgrade now?",
                "Upgrade Available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return VersionCheckResult.Exit;

            try
            {
                // Try graceful shutdown first
                if (!process.CloseMainWindow())
                {
                    process.Kill();
                }

                if (!process.WaitForExit(3000))
                {
                    MessageBox.Show(
                        "Could not close the previous version.\n" +
                        "Please close it manually and run the new version again.",
                        "Upgrade Warning",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return VersionCheckResult.Exit;
                }

                return VersionCheckResult.Restart;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Upgrade failed: {ex.Message}\n\n" +
                    "Please close the old version manually and try again.",
                    "Upgrade Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return VersionCheckResult.Exit;
            }
        }
    }
}
