using System;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Management;
using AudioEndPointController;

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
        private NotifyIcon trayIcon;
        private bool isRunning = true;
        private int checkInterval = 5000;
        private DateTime lastCheckTime = DateTime.MinValue;
        private readonly IAudioController audioController;

        public PreventLockApp()
        {
            audioController = new AudioController();
            
            // ==== Tray Icon Setup ====
            trayIcon = new NotifyIcon()
            {
                Icon = CreateColoredIcon(Color.Gray),
                Text = "Spotify Prevent Lock\nTimer: " + (checkInterval / 1000) + "s",
                Visible = true,
                ContextMenuStrip = CreateContextMenu()
            };

            new Thread(WorkerThread) { IsBackground = true }.Start();
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
            
            dialog.Controls.AddRange(new Control[] { label, numericBox, okButton });
            
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                checkInterval = (int)numericBox.Value * 1000;
                trayIcon.Text = "Spotify Prevent Lock\nTimer: " + (checkInterval / 1000) + "s";
            }
        }

        private void WorkerThread()
        {
            while (isRunning)
            {
                if ((DateTime.Now - lastCheckTime).TotalMilliseconds >= checkInterval)
                {
                    lastCheckTime = DateTime.Now;
                    bool isPlaying = IsMediaPlaying();

                    SetThreadExecutionState(isPlaying 
                        ? ExecutionState.ES_DISPLAY_REQUIRED | ExecutionState.ES_CONTINUOUS
                        : ExecutionState.ES_CONTINUOUS);

                    trayIcon.Icon = CreateColoredIcon(isPlaying ? Color.LimeGreen : Color.Gray);
                    
                    if (isPlaying)
                    {
                        mouse_event(0x0001, 0, 0, 0, IntPtr.Zero);
                    }
                }
                Thread.Sleep(100);
            }
        }

        private bool IsMediaPlaying()
        {
            try
            {
                // Check if any audio is playing through the default device
                foreach (var device in audioController.GetPlaybackDevices())
                {
                    if (device.IsDefault && device.AudioMeterInformation.MasterPeakValue > 0.01f)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                // Fallback to process check if audio API fails
                return IsProcessRunning("Spotify");
            }
        }

        private bool IsProcessRunning(string processName)
        {
            var query = $"SELECT Name FROM Win32_Process WHERE Name = '{processName}.exe'";
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();
            return results.Count > 0;
        }

        private Icon CreateColoredIcon(Color color)
        {
            using var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.Clear(color);
            return Icon.FromHandle(bmp.GetHicon());
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
