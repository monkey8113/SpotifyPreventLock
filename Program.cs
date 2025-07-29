using System;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace SpotifyPreventLock
{
    public class PreventLockApp : Form
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
                Text = $"Spotify Prevent Lock\nCheck interval: {checkInterval/1000}s",
                Visible = true
            };

            // ==== Right-Click Menu ====
            trayMenu = new ContextMenuStrip();
            
            // Custom Interval Input
            var customIntervalItem = new ToolStripMenuItem("Set Custom Interval...");
            customIntervalItem.Click += OnCustomIntervalClick;
            
            trayMenu.Items.Add(customIntervalItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Exit", null, OnExit);
            
            if (trayIcon != null)
            {
                trayIcon.ContextMenuStrip = trayMenu;
            }

            // ==== Start Worker Thread ====
            new Thread(WorkerThread).Start();
        }

        private void OnCustomIntervalClick(object? sender, EventArgs e)
        {
            using var inputDialog = new Form()
            {
                Text = "Set Check Interval",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Width = 300,
                Height = 150,
                StartPosition = FormStartPosition.CenterScreen
            };

            var numericUpDown = new NumericUpDown()
            {
                Minimum = 1,
                Maximum = 3600,
                Value = checkInterval / 1000,
                Width = 100,
                Top = 20,
                Left = 100
            };
            
            var label = new Label()
            {
                Text = "Seconds (1-3600):",
                Top = 25,
                Left = 20
            };
            
            var okButton = new Button()
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Top = 70,
                Left = 100
            };
            
            inputDialog.Controls.AddRange(new Control[] { label, numericUpDown, okButton });
            
            if (inputDialog.ShowDialog() == DialogResult.OK)
            {
                SetInterval((int)numericUpDown.Value * 1000);
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
                    
                    if (IsSpotifyPlaying())
                    {
                        SetThreadExecutionState(ExecutionState.ES_DISPLAY_REQUIRED | 
                                             ExecutionState.ES_CONTINUOUS);
                        mouse_event(0x0001, 0, 0, 0, IntPtr.Zero);
                        
                        if (trayIcon != null)
                        {
                            trayIcon.Icon = CreateColoredIcon(Color.LimeGreen);
                        }
                    }
                    else
                    {
                        SetThreadExecutionState(ExecutionState.ES_CONTINUOUS);
                        
                        if (trayIcon != null)
                        {
                            trayIcon.Icon = CreateColoredIcon(Color.Gray);
                        }
                    }
                }
                Thread.Sleep(100);
            }
        }

        // ==== Helper Methods ====
        private bool IsSpotifyPlaying()
        {
            try 
            {
                return System.Diagnostics.Process.GetProcessesByName("Spotify").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private void SetInterval(int milliseconds)
        {
            checkInterval = milliseconds;
            if (trayIcon != null)
            {
                trayIcon.Text = $"Spotify Prevent Lock\nCheck interval: {checkInterval/1000}s";
            }
        }

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
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                trayIcon?.Dispose();
                trayMenu?.Dispose();
            }
            base.Dispose(disposing);
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PreventLockApp());
        }
    }
}
