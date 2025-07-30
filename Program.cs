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
        public int CheckInterval { get; set; } = 300000; // Default 5 minutes
    }

    public class PreventLockApp : ApplicationContext
    {
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, IntPtr dwExtraInfo);

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
        private DateTime lastCheckTime = DateTime.MinValue;
        private readonly string settingsPath;
        private readonly string settingsDirectory;
        private const string AppVersion = "v1.0.0";
        private readonly Font versionFont;

        public PreventLockApp()
        {
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
                Text = $"Spotify Prevent Lock {AppVersion}\nTimer: {settings.CheckInterval / 1000}s",
                Visible = true,
                ContextMenuStrip = CreateContextMenu()
            };

            new Thread(WorkerThreadMethod) { IsBackground = true }.Start();
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
                // Load from embedded resources
                string iconName = isPlaying ? "SpotifyPreventLock.app.ico" : "SpotifyPreventLock.appoff.ico";
                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(iconName);
                if (stream != null)
                {
                    return new Icon(stream);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading embedded icon: {ex.Message}");
            }

            // Fallback to generated icon
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

            var timerItem = new ToolStripMenuItem("Timer");
            timerItem.DropDownItems.Add("Set Custom Time...", null, (s, e) => ShowTimerDialog());
            menu.Items.Add(timerItem);

            var startupItem = new ToolStripMenuItem("Start with Windows");
            startupItem.Click += (s, e) => ToggleStartup();
            UpdateStartupMenuItem(startupItem);
            menu.Items.Add(startupItem);

            var versionItem = new ToolStripMenuItem(AppVersion)
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
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

                if (key != null)
                {
                    if (IsInStartup())
                    {
                        key.DeleteValue("SpotifyPreventLock", false);
                    }
                    else
                    {
                        // Use explorer.exe as parent to ensure proper UI context
                        key.SetValue("SpotifyPreventLock", 
                            $"explorer.exe \"{Application.ExecutablePath}\"");
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

        private void UpdateStartupMenuItem(ToolStripMenuItem item = null)
        {
            var menuItem = item ?? (trayIcon.ContextMenuStrip?.Items[1] as ToolStripMenuItem);
            if (menuItem != null)
            {
                bool isEnabled = IsInStartup();
                menuItem.Checked = isEnabled;
                menuItem.Text = isEnabled ? "✓ Start with Windows" : "Start with Windows";
            }
        }

        private bool IsInStartup()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", false);
                return key?.GetValue("SpotifyPreventLock") != null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking startup: {ex.Message}");
                return false;
            }
        }

        // [Rest of the methods remain exactly the same...]
        // ShowTimerDialog(), WorkerThreadMethod(), UpdateSystemState(), 
        // IsSpotifyActive(), OnExit(), Dispose() unchanged from your original code

        private void ShowTimerDialog() { /* unchanged */ }
        private void WorkerThreadMethod() { /* unchanged */ }
        private void UpdateSystemState(bool isPlaying) { /* unchanged */ }
        private bool IsSpotifyActive() { /* unchanged */ }
        private void OnExit() { /* unchanged */ }
        protected override void Dispose(bool disposing) { /* unchanged */ }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            // Wait for system tray to initialize
            Thread.Sleep(3000);
            
            Application.Run(new PreventLockApp());
        }
    }
}
