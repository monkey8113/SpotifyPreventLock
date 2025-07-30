using System;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Win32;
using System.Security.Principal;

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

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

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
        private const string AppVersion = "v1.0.0";
        private readonly Font versionFont;

        public PreventLockApp()
        {
            // Initialize cached font
            versionFont = new Font("Segoe UI", 8.25f, FontStyle.Italic);

            // Initialize paths
            settingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SpotifyPreventLock");
            Directory.CreateDirectory(settingsDirectory);
            settingsPath = Path.Combine(settingsDirectory, "settings.json");

            // Load settings
            settings = LoadSettings();
            isRunning = true;

            // Initialize tray icon
            trayIcon = new NotifyIcon()
            {
                Icon = LoadTrayIcon(false),
                Text = $"Spotify Prevent Lock {AppVersion}\nTimer: {settings.CheckInterval / 1000}s",
                Visible = true,
                ContextMenuStrip = CreateContextMenu()
            };

            // Start worker thread
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
                string iconFile = isPlaying ? "app.ico" : "appoff.ico";
                if (File.Exists(iconFile))
                {
                    using var stream = File.OpenRead(iconFile);
                    return new Icon(stream);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading icon: {ex.Message}");
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
            
            var timerItem = new ToolStripMenuItem("Timer");
            timerItem.DropDownItems.Add("Set Custom Time...", null, (s, e) => ShowTimerDialog());
            menu.Items.Add(timerItem);
            
            var startupItem = new ToolStripMenuItem("Start with Windows");
            startupItem.Click += (s, e) => ToggleStartup();
            menu.Items.Add(startupItem);
            
            var versionItem = new ToolStripMenuItem(AppVersion)
            {
                Enabled = false,
                Font = versionFont
            };
            menu.Items.Add(versionItem);
            
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => OnExit());
            
            UpdateStartupMenuItem(); // Initialize the menu item state
            return menu;
        }

        private void ToggleStartup()
        {
            try
            {
                if (IsInStartup())
                {
                    RemoveStartupTask();
                }
                else
                {
                    if (!IsUserAdministrator())
                    {
                        if (MessageBox.Show("This requires administrator permissions. Elevate now?", 
                            "Permission Needed", MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            RestartAsAdmin();
                            return;
                        }
                        return;
                    }
                    CreateStartupTask();
                }
                UpdateStartupMenuItem();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error toggling startup: {ex.Message}");
                MessageBox.Show($"Failed to configure startup: {ex.Message}");
            }
        }

        private void UpdateStartupMenuItem()
        {
            if (trayIcon.ContextMenuStrip?.Items[1] is ToolStripMenuItem menuItem)
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
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = "/query /tn \"SpotifyPreventLock\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    }
                };
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                return output.Contains("SpotifyPreventLock");
            }
            catch
            {
                return false;
            }
        }

        private void CreateStartupTask()
        {
            try
            {
                string arguments = $"/create /tn \"SpotifyPreventLock\" /tr \"\\\"{Application.ExecutablePath}\\\"\" /sc onlogon /rl highest /delay 0000:05 /f";
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = arguments,
                        Verb = IsUserAdministrator() ? "runas" : "",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating task: {ex.Message}");
                throw;
            }
        }

        private void RemoveStartupTask()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = "/delete /tn \"SpotifyPreventLock\" /f",
                        Verb = IsUserAdministrator() ? "runas" : "",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error removing task: {ex.Message}");
                throw;
            }
        }

        private bool IsUserAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private void RestartAsAdmin()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                UseShellExecute = true,
                Verb = "runas"
            };

            try
            {
                Process.Start(startInfo);
                Application.Exit();
            }
            catch
            {
                MessageBox.Show("Failed to restart with admin rights");
            }
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
                trayIcon.Text = $"Spotify Prevent Lock {AppVersion}\nTimer: {settings.CheckInterval / 1000}s";
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
                else if ((DateTime.Now - lastCheckTime).TotalMilliseconds >= settings.CheckInterval)
                {
                    lastCheckTime = DateTime.Now;
                    UpdateSystemState(isPlaying);
                    
                    if (isPlaying)
                    {
                        mouse_event(0x0001, 0, 0, 0, IntPtr.Zero);
                    }
                }
                
                Thread.Sleep(100);
            }
        }

        private void UpdateSystemState(bool isPlaying)
        {
            SetThreadExecutionState(isPlaying 
                ? ExecutionState.ES_DISPLAY_REQUIRED | ExecutionState.ES_CONTINUOUS
                : ExecutionState.ES_CONTINUOUS);
            
            var icon = LoadTrayIcon(isPlaying);
            if (icon != null)
            {
                trayIcon.Icon = icon;
            }
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
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking Spotify: {ex.Message}");
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
            
            // Wait for system tray to initialize
            Thread.Sleep(3000);
            
            Application.Run(new PreventLockApp());
        }
    }
}
