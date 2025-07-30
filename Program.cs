using System;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Reflection;
using System.Linq;
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
            // Get dynamic version from assembly
            appVersion = GetAppVersion();
            versionFont = new Font("Segoe UI", 8.25f, FontStyle.Italic);

            settingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SpotifyPreventLock");
            Directory.CreateDirectory(settingsDirectory);
            settingsPath = Path.Combine(settingsDirectory, "settings.json");

            settings = LoadSettings();
            isRunning = true;

            trayIcon = new NotifyIcon()
            {
                Icon = LoadTrayIcon(false),
                Text = $"Spotify Prevent Lock {appVersion}\nCheck Interval: {settings.CheckInterval}ms",
                Visible = true,
                ContextMenuStrip = CreateContextMenu()
            };

            // Validate startup entry on launch
            ValidateStartupEntry();

            new Thread(WorkerThreadMethod) { IsBackground = true }.Start();
        }

        private static string GetAppVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return $"v{version.Major}.{version.Minor}.{version.Build}";
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
                using var key = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

                if (key != null)
                {
                    if (IsInStartup())
                    {
                        key.DeleteValue("SpotifyPreventLock", false);
                    }
                    else
                    {
                        // Store path + version + timestamp
                        string valueData = $"\"{Application.ExecutablePath}\"|{appVersion}|{DateTime.Now.Ticks}";
                        key.SetValue("SpotifyPreventLock", valueData);
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
                using var key = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", false);
                
                if (key?.GetValue("SpotifyPreventLock") is string valueData)
                {
                    string[] parts = valueData.Split('|');
                    
                    // Basic format check
                    if (parts.Length < 2) return false;
                    
                    string storedPath = parts[0].Trim('"');
                    string storedVersion = parts[1];
                    
                    // Check if path exists and version matches
                    return File.Exists(storedPath) && 
                           PathsEqual(storedPath, Application.ExecutablePath) &&
                           storedVersion == appVersion;
                }
            }
            catch { /* Error handling */ }
            return false;
        }

        private static bool PathsEqual(string path1, string path2)
        {
            return Path.GetFullPath(path1)
                .Equals(Path.GetFullPath(path2), StringComparison.OrdinalIgnoreCase);
        }

        private void ValidateStartupEntry()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                
                if (key?.GetValue("SpotifyPreventLock") is string valueData)
                {
                    string[] parts = valueData.Split('|');
                    
                    // Check if this is an old format entry
                    if (parts.Length < 2)
                    {
                        // Convert old format to new format
                        string newValue = $"\"{parts[0].Trim('"')}\"|{appVersion}|{DateTime.Now.Ticks}";
                        key.SetValue("SpotifyPreventLock", newValue);
                    }
                    else if (parts.Length >= 2)
                    {
                        string storedPath = parts[0].Trim('"');
                        string storedVersion = parts[1];
                        
                        // Case 1: Path exists but isn't this executable
                        if (File.Exists(storedPath) && !PathsEqual(storedPath, Application.ExecutablePath))
                        {
                            // Keep old entry but mark as inactive
                            key.SetValue("SpotifyPreventLock_OLD", valueData);
                            key.DeleteValue("SpotifyPreventLock");
                        }
                        // Case 2: Version mismatch
                        else if (storedVersion != appVersion)
                        {
                            // Update to current version
                            string newValue = $"\"{Application.ExecutablePath}\"|{appVersion}|{DateTime.Now.Ticks}";
                            key.SetValue("SpotifyPreventLock", newValue);
                        }
                    }
                }
            }
            catch { /* Silent failure */ }
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
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            // Clean up old registry entries before starting
            CleanupOldRegistryEntries();
            
            // Wait for system tray to initialize
            Thread.Sleep(3000);
            
            Application.Run(new PreventLockApp());
        }

        private static void CleanupOldRegistryEntries()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                
                if (key == null) return;

                // Get all values that look like our app
                var values = key.GetValueNames()
                    .Where(name => name.StartsWith("SpotifyPreventLock"))
                    .ToList();

                string currentExe = Path.GetFileName(Application.ExecutablePath);

                // Clean up invalid entries
                foreach (var valueName in values)
                {
                    if (key.GetValue(valueName) is string valueData)
                    {
                        string[] parts = valueData.Split('|');
                        if (parts.Length > 0)
                        {
                            string storedPath = parts[0].Trim('"');
                            
                            // Delete if:
                            // 1. It's not the current EXE path, OR
                            // 2. It's an old format entry
                            if ((!File.Exists(storedPath) || 
                                !Path.GetFileName(storedPath).Equals(currentExe, StringComparison.OrdinalIgnoreCase)) &&
                                valueName != "SpotifyPreventLock")
                            {
                                key.DeleteValue(valueName, false);
                            }
                        }
                    }
                }
            }
            catch { /* Silent failure is okay */ }
        }
    }
}
