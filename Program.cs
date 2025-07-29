using System;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Linq;
using System.Management;

namespace SpotifyPreventLock
{
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
        private NotifyIcon? trayIcon;
        private ContextMenuStrip? trayMenu;
        private bool isRunning = true;
        private int checkInterval = 5000; // Default 5 seconds
        private DateTime lastCheckTime = DateTime.MinValue;

        public PreventLockApp()
        {
            // ==== Tray Icon Setup ====
            trayIcon = new NotifyIcon()
            {
                Icon = CreateColoredIcon(Color.Gray),
                Text = "Spotify Prevent Lock\nTimer: " + (checkInterval / 1000) + "s",
                Visible = true
            };

            // ==== Right-Click Menu ====
            trayMenu = new ContextMenuStrip();
            
            // Timer Settings
            var timerMenu = new ToolStripMenuItem("Timer");
            timerMenu.DropDownItems.Add("Set Custom Time...", null, ShowTimerDialog);
            
            trayMenu.Items.Add(timerMenu);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Exit", null, OnExit);
            
            trayIcon.ContextMenuStrip = trayMenu;

            // ==== Start Worker Thread ====
            new Thread(WorkerThread).Start();
        }

        private void ShowTimerDialog(object? sender, EventArgs e)
        {
            using var inputDialog = new Form()
            {
                Text = "Set Timer",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Width = 250,
                Height = 150,
                StartPosition = FormStartPosition.CenterScreen,
                ShowInTaskbar = false
            };

            var numericUpDown = new NumericUpDown()
            {
                Minimum = 1,
                Maximum = 3600,
                Value = checkInterval / 1000,
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
            
            inputDialog.Controls.AddRange(new Control[] { label, numericUpDown, okButton });
            
            if (inputDialog.ShowDialog() == DialogResult.OK)
            {
                checkInterval = (int)numericUpDown.Value * 1000;
                trayIcon!.Text = "Spotify Prevent Lock\nTimer: " + (checkInterval / 1000) + "s";
            }
        }

        // ==== Core Functionality ====
        private void WorkerThread()
        {
            while (isRunning)
            {
                if ((DateTime.Now - lastCheckTime).TotalMilliseconds >= checkInterval)
                {
                    lastCheckTime = DateTime.Now;
                    
                    if (IsMediaPlaying())
                    {
                        SetThreadExecutionState(ExecutionState.ES_DISPLAY_REQUIRED | ExecutionState.ES_CONTINUOUS);
                        mouse_event(0x0001, 0, 0, 0, IntPtr.Zero);
                        trayIcon!.Icon = CreateColoredIcon(Color.LimeGreen);
                    }
                    else
                    {
                        SetThreadExecutionState(ExecutionState.ES_CONTINUOUS);
                        trayIcon!.Icon = CreateColoredIcon(Color.Gray);
                    }
                }
                Thread.Sleep(100);
            }
        }

        // ==== Media Detection ====
        private bool IsMediaPlaying()
        {
            try
            {
                // Check using Windows Media Session
                var sessionManager = new System.Management.ManagementObjectSearcher(
                    "SELECT * FROM Win32_Process WHERE Name = 'Spotify.exe'");
                
                var processes = sessionManager.Get().Cast<ManagementObject>();
                if (!processes.Any()) return false;

                // More accurate check for actual playback (Windows 10/11 compatible)
                return GetMediaSessionState();
            }
            catch
            {
                return false;
            }
        }

        private bool GetMediaSessionState()
        {
            // Placeholder - in a full implementation you would:
            // 1. Use Windows.Media.Control APIs to detect actual playback state
            // 2. Check if Spotify is currently playing audio
            // For now, we'll assume if Spotify exists, media is playing
            return true;
        }

        // ==== Helper Methods ====
        private Icon CreateColoredIcon(Color color)
        {
            using var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.Clear(color);
            return Icon.FromHandle(bmp.GetHicon());
        }

        private void OnExit(object? sender, EventArgs e)
        {
            isRunning = false;
            trayIcon!.Visible = false;
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
            Application.Run(new PreventLockApp()); // Uses ApplicationContext instead of Form
        }
    }
}
