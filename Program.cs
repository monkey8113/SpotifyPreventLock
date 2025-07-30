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
        public int CheckInterval { get; set; } = 2000;
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

            CleanupRegistryEntries();
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
                return new AppSettings();
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
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(settingsToSave));
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
                return stream != null ? new Icon(stream) : CreateCircleIcon(isPlaying ? Color.LimeGreen : Color.Gray);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading icon: {ex.Message}");
                return CreateCircleIcon(isPlaying ? Color.LimeGreen : Color.Gray);
            }
        }

        private Icon CreateCircleIcon(Color color)
        {
            using var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                g.FillEllipse(new SolidBrush(color), 0, 0, 15, 15);
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

            menu.Items.Add(new ToolStripMenuItem(appVersion) { Enabled = false, Font = versionFont });
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

                if (key == null) return;

                if (IsInStartup())
                {
                    key.DeleteValue("SpotifyPreventLock", false);
                }
                else
                {
                    key.SetValue("SpotifyPreventLock", 
                        $"\"{Application.ExecutablePath}\"|{appVersion}|{DateTime.Now.Ticks}");
                }
                UpdateStartupMenuItem();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Startup toggle error: {ex.Message}");
                MessageBox.Show("Failed to update startup settings.", "Startup Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                
                if (key?.GetValue("SpotifyPreventLock") is not string valueData)
                    return false;

                var parts = valueData.Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    return false;

                string storedPath = parts[0].Trim('"');
                string storedVersion = parts[1];
                
                return File.Exists(storedPath) && 
                       Path.GetFullPath(storedPath).Equals(
                           Path.GetFullPath(Application.ExecutablePath), 
                           StringComparison.OrdinalIgnoreCase) &&
                       storedVersion == appVersion;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Startup check error: {ex.Message}");
                return false;
            }
        }

        private void CleanupRegistryEntries()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                
                if (key == null) return;

                foreach (var valueName in key.GetValueNames()
                    .Where(n => n.StartsWith("SpotifyPreventLock", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        key.DeleteValue(valueName, false);
                    }
                    catch { /* Continue if deletion fails */ }
                }
            }
            catch { /* Silent failure is acceptable */ }
        }

        private void ValidateStartupEntry()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                
                if (key?.GetValue("SpotifyPreventLock") is not string valueData)
                    return;

                var parts = valueData.Split('|', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || 
                    !File.Exists(parts[0].Trim('"')) || 
                    !Path.GetFullPath(parts[0].Trim('"')).Equals(
                        Path.GetFullPath(Application.ExecutablePath), 
                        StringComparison.OrdinalIgnoreCase))
                {
                    key.DeleteValue("SpotifyPreventLock", false);
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

            var numericUpDown = new NumericUpDown()
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

            dialog.Controls.Add(infoLabel);
            dialog.Controls.Add(label);
            dialog.Controls.Add(numericUpDown);
            dialog.Controls.Add(okButton);

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                settings.CheckInterval = (int)numericUpDown.Value;
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
                return Process.GetProcessesByName("Spotify")
                    .Any(proc => proc != null && 
                        !string.IsNullOrEmpty(proc.MainWindowTitle) &&
                        !proc.MainWindowTitle.Contains("Spotify", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Spotify check error: {ex.Message}");
                return false;
            }
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
            Application.Run(new PreventLockApp());
        }
    }
}
