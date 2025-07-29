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
            settingsPath = Path.Combine(settingsDirectory, "settings.json");

            // Load settings
            settings = LoadSettings();
            isRunning = true;

            // Initialize tray icon
            trayIcon = new NotifyIcon()
            {
                Icon = LoadTrayIcon(false), // Start with inactive icon
                Text = $"Spotify Prevent Lock {AppVersion}\nTimer: {settings.CheckInterval / 1000}s",
                Visible = true,
                ContextMenuStrip = CreateContextMenu()
            };

            // Start worker thread
            new Thread(WorkerThread) { IsBackground = true }.Start();
        }

        private Icon LoadTrayIcon(bool isPlaying)
        {
            try
            {
                string iconFile = isPlaying ? "app.ico" : "appoff.ico";
                if (File.Exists(iconFile))
                {
                    return new Icon(iconFile);
                }
            }
            catch { /* Fall through to default */ }
            
            // Fallback to colored circles
            return CreateCircleIcon(isPlaying ? Color.LimeGreen : Color.Gray);
        }

        private Icon CreateCircleIcon(Color color)
        {
            using var bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 0, 0, 15, 15);
            return Icon.FromHandle(bmp.GetHicon());
        }

        // ... [Rest of your existing methods remain unchanged] ...
        
        private void UpdateSystemState(bool isPlaying)
        {
            SetThreadExecutionState(isPlaying 
                ? ExecutionState.ES_DISPLAY_REQUIRED | ExecutionState.ES_CONTINUOUS
                : ExecutionState.ES_CONTINUOUS);
            
            trayIcon.Icon = LoadTrayIcon(isPlaying);
        }

        // ... [Other methods unchanged] ...
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
