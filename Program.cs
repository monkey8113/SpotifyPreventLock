using System;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Win32;

namespace SpotifyPreventLock
{
    public class AppSettings
    {
        public int CheckInterval { get; set; } = 300000; // Default 5 minutes
    }

    public class PreventLockApp : ApplicationContext
    {
        // Windows API Imports
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

        // App Components
        private readonly NotifyIcon trayIcon;
        private volatile bool isRunning;
        private readonly AppSettings settings;
        private DateTime lastCheckTime = DateTime.MinValue;
        private readonly string settingsPath;
        private static Mutex mutex;

        public PreventLockApp()
        {
            // Single instance check
            bool createdNew;
            mutex = new Mutex(true, "{8F6F0AC4-B9A1-45FD-A8CF-72F04E6BDE8F}", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("Application is already running!");
                Environment.Exit(0);
            }

            // Load settings
            settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SpotifyPreventLock", "settings.json");
            settings = LoadSettings();

            // Initialize tray icon with retry logic
            trayIcon = new NotifyIcon();
            InitializeTrayIcon();

            // Start worker thread
            new Thread(WorkerThreadMethod) { IsBackground = true }.Start();
        }

        private void InitializeTrayIcon()
        {
            // Retry logic for icon visibility
            int retries = 0;
            while (retries < 3)
            {
                try
                {
                    trayIcon.Icon = LoadTrayIcon(false);
                    trayIcon.Text = $"Spotify Prevent Lock\nTimer: {settings.CheckInterval / 1000}s";
                    trayIcon.Visible = true;
                    trayIcon.ContextMenuStrip = CreateContextMenu();
                    break;
                }
                catch
                {
                    retries++;
                    Thread.Sleep(1000); // Wait 1 second before retry
                }
            }
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
                        key.DeleteValue("SpotifyPreventLock");
                    }
                    else
                    {
                        // Updated with 10-second delay
                        key.SetValue("SpotifyPreventLock", 
                            $"cmd /c \"timeout 10 && start \"\" \"{Application.ExecutablePath}\"\"");
                    }
                    UpdateStartupMenuItem();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Startup toggle error: {ex.Message}");
            }
        }

        // ... [Keep all other methods unchanged from your original code] ...

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                trayIcon?.Dispose();
                mutex?.ReleaseMutex();
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
