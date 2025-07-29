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
        public int CheckInterval { get; set; } = 5000; // Default 5 seconds
    }

    public class PreventLockApp : ApplicationContext
    {
        // [Windows API imports remain the same...]

        private NotifyIcon trayIcon;
        private bool isRunning = true;
        private AppSettings settings;
        private DateTime lastCheckTime = DateTime.MinValue;
        private readonly string settingsPath;
        private readonly string settingsDirectory;

        public PreventLockApp()
        {
            // Initialize paths safely
            settingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SpotifyPreventLock");
            settingsPath = Path.Combine(settingsDirectory, "settings.json");

            LoadSettings();

            // [Rest of constructor remains the same...]
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);
                    settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                else
                {
                    settings = new AppSettings();
                    // Safe directory creation
                    if (!string.IsNullOrEmpty(settingsDirectory))
                    {
                        Directory.CreateDirectory(settingsDirectory);
                    }
                }
            }
            catch
            {
                settings = new AppSettings();
            }
        }

        private void SaveSettings()
        {
            try
            {
                if (!string.IsNullOrEmpty(settingsDirectory))
                {
                    Directory.CreateDirectory(settingsDirectory);
                    string json = JsonSerializer.Serialize(settings);
                    File.WriteAllText(settingsPath, json);
                }
            }
            catch { /* Ignore save errors */ }
        }

        // [Rest of the methods remain the same...]

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
