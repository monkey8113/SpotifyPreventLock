using System;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using System.Text.Json;
using System.Diagnostics;

namespace SpotifyPreventLock
{
    public class AppSettings
    {
        public int CheckInterval { get; set; } = 300000; // Default 5 minutes
    }

    public class PreventLockApp : ApplicationContext
    {
        // ==== Windows API Imports ====
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, IntPtr dwExtraInfo);

        [DllImport("kernel32.dll")]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

        [Flags]
        private enum ExecutionState : uint
        {
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
        }

        // ==== App Settings ====
        private readonly NotifyIcon trayIcon;
        private volatile bool isRunning;
        private readonly AppSettings settings;
        private DateTime lastCheckTime = DateTime.MinValue;
        private readonly string settingsPath;
        private readonly string settingsDirectory;

        public PreventLockApp()
        {
            // Initialize paths
            settingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SpotifyPreventLock");
            settingsPath = Path.Combine(settingsDirectory, "settings.json");

            // Load settings
            settings = LoadSettings();
            isRunning = true;

            // Initialize tray icon
            trayIcon = new NotifyIcon()
            {
                Icon = CreateCircleIcon(Color.Gray),
                Text = $"Spotify Prevent Lock\nTimer: {settings.CheckInterval / 1000}s",
                Visible = true,
                ContextMenuStrip = CreateContextMenu()
            };

            // Start worker thread
            new Thread(WorkerThread) { IsBackground = true }.Start();
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
                
                Directory.CreateDirectory(settingsDirectory);
                return new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        private void SaveSettings()
        {
            try
            {
                string json = JsonSerializer.Serialize(settings);
                File.WriteAllText(settingsPath, json);
            }
            catch { /* Ignore save errors */ }
        }

        private Icon CreateCircleIcon(Color color)
        {
            using var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 0, 0, 15, 15);
            return Icon.FromHandle(bmp.GetHicon());
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();
            
            var timerItem = new ToolStripMenuItem("Timer");
            timerItem.DropDownItems.Add("Set Custom Time...", null, (s, e) => ShowTimerDialog());
            
            menu.Items.Add(timerItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => OnExit());
            
            return menu;
        }

        private void ShowTimerDialog()
        {
            using var dialog = new Form()
            {
                Text = "Set Timer",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Width = 250,
                Height = 150,
                StartPosition = FormStartPosition.CenterScreen,
                ShowInTaskbar = false
            };

            var numericBox = new NumericUpDown()
            {
                Minimum = 1,
                Maximum = 3600,
                Value = settings.CheckInterval / 1000,
                Width = 80,
                Top = 40,
                Left = 100
            };
            
            var label = new Label()
            {
                Text = "Time (s):",
                Top = 45,
                Left = 30,
                Width = 60
            };
            
            var okButton = new Button()
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Top = 80,
                Left = 90,
                Width = 75
            };
            
            dialog.Controls.AddRange(new Control[] { label, numericBox, okButton });
            
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                settings.CheckInterval = (int)numericBox.Value * 1000;
                trayIcon.Text = $"Spotify Prevent Lock\nTimer: {settings.CheckInterval / 1000}s";
                SaveSettings();
            }
        }

        private void WorkerThread()
        {
            bool wasPlaying = false;
            
            while (isRunning)
            {
                bool isPlaying = IsSpotifyActive();
                
                // Immediate response to state changes
                if (isPlaying != wasPlaying)
                {
                    UpdateSystemState(isPlaying);
                    wasPlaying = isPlaying;
                }
                // Periodic check based on interval
                else if ((DateTime.Now - lastCheckTime).TotalMilliseconds >= settings.CheckInterval)
                {
                    lastCheckTime = DateTime.Now;
                    UpdateSystemState(isPlaying);
                    
                    // Reset idle timer if playing
                    if (isPlaying)
                    {
                        mouse_event(0x0001, 0, 0, 0, IntPtr.Zero);
                    }
                }
                
                Thread.Sleep(100); // Quick responsiveness check
            }
        }

        private void UpdateSystemState(bool isPlaying)
        {
            SetThreadExecutionState(isPlaying 
                ? ExecutionState.ES_DISPLAY_REQUIRED | ExecutionState.ES_CONTINUOUS
                : ExecutionState.ES_CONTINUOUS);
            
            trayIcon.Icon = CreateCircleIcon(isPlaying ? Color.LimeGreen : Color.Gray);
        }

        private bool IsSpotifyActive()
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName("Spotify"))
                {
                    if (!string.IsNullOrWhiteSpace(proc.MainWindowTitle) && 
                        !proc.MainWindowTitle.Contains("Spotify"))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
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
