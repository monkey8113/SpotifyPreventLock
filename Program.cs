using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;
using System.Threading;
using System.Linq;

namespace SpotifyPreventLock
{
    public class PreventLockApp : Form
    {
        // DLL imports for media session and system lock prevention
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, IntPtr dwExtraInfo);
        
        [DllImport("kernel32.dll")]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);
        
        [Flags]
        private enum ExecutionState : uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
        }

        // Constants for media session
        private const string SpotifyDesktopApp = "Spotify.exe";
        private const int CheckInterval = 1000; // 1 second
        private const int DefaultIdleTimeout = 5 * 60 * 1000; // 5 minutes

        // App components
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem exitMenuItem;
        private ToolStripMenuItem timeoutMenuItem;
        private System.Threading.Timer activityTimer;
        private int idleTimeout = DefaultIdleTimeout;
        private bool mediaPlaying = false;

        public PreventLockApp()
        {
            InitializeComponents();
            StartMonitoring();
        }

        private void InitializeComponents()
        {
            // Create tray icon and menu
            trayMenu = new ContextMenuStrip();
            exitMenuItem = new ToolStripMenuItem("Exit");
            timeoutMenuItem = new ToolStripMenuItem("Idle Timeout: 5 minutes");
            
            // Add timeout options
            for (int i = 1; i <= 10; i++)
            {
                var item = new ToolStripMenuItem($"{i} minutes");
                item.Click += (s, e) => 
                {
                    idleTimeout = i * 60 * 1000;
                    timeoutMenuItem.Text = $"Idle Timeout: {i} minutes";
                };
                timeoutMenuItem.DropDownItems.Add(item);
            }
            
            trayMenu.Items.Add(timeoutMenuItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(exitMenuItem);
            
            exitMenuItem.Click += (s, e) => Application.Exit();

            trayIcon = new NotifyIcon
            {
                Text = "Spotify Prevent Lock",
                Icon = new Icon(SystemIcons.Application, 40, 40),
                ContextMenuStrip = trayMenu,
                Visible = true
            };

            // Set up timer to check for idle state
            activityTimer = new System.Threading.Timer(CheckActivity, null, CheckInterval, CheckInterval);
        }

        private void StartMonitoring()
        {
            // Start monitoring media sessions in a background thread
            var monitorThread = new Thread(() =>
            {
                while (true)
                {
                    bool wasPlaying = mediaPlaying;
                    mediaPlaying = IsMediaPlaying();

                    // Update tray icon color
                    trayIcon.Icon = mediaPlaying 
                        ? CreateColoredIcon(Color.LimeGreen) 
                        : CreateColoredIcon(Color.LightGray);

                    if (mediaPlaying && !wasPlaying)
                    {
                        SetThreadExecutionState(ExecutionState.ES_CONTINUOUS | 
                                               ExecutionState.ES_DISPLAY_REQUIRED | 
                                               ExecutionState.ES_SYSTEM_REQUIRED);
                    }
                    else if (!mediaPlaying && wasPlaying)
                    {
                        SetThreadExecutionState(ExecutionState.ES_CONTINUOUS);
                    }

                    Thread.Sleep(2000); // Check every 2 seconds
                }
            })
            {
                IsBackground = true
            };
            monitorThread.Start();
        }

        private bool IsMediaPlaying()
        {
            try
            {
                // Using Windows Media Session to detect playback
                var manager = new System.Management.ManagementObjectSearcher(
                    "SELECT * FROM Win32_Process WHERE Name = 'Spotify.exe'");
                
                var processes = manager.Get().Cast<System.Management.ManagementObject>();
                if (processes.Any())
                {
                    // More accurate check using Windows Media Session API
                    return CheckWindowsMediaSessions();
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool CheckWindowsMediaSessions()
        {
            // This would use Windows.Media.Control APIs in a real implementation
            // For simplicity, we'll assume media is playing if Spotify is running
            // In a full implementation, you would actually check playback state
            
            // Placeholder - in reality you'd use Windows.Media.Control namespace
            // to check actual playback state from system media sessions
            return true;
        }

        private void CheckActivity(object state)
        {
            if (!mediaPlaying) return;

            var lastInput = GetLastInputTime();
            if (lastInput > idleTimeout)
            {
                // Simulate tiny mouse movement to reset idle timer
                mouse_event(0x0001, 1, 1, 0, IntPtr.Zero);
                mouse_event(0x0001, 0, 0, 0, IntPtr.Zero);
            }
        }

        private int GetLastInputTime()
        {
            var lastInputInfo = new LASTINPUTINFO();
            lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);
            GetLastInputInfo(ref lastInputInfo);
            return Environment.TickCount - (int)lastInputInfo.dwTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        private Icon CreateColoredIcon(Color color)
        {
            using (var bmp = new Bitmap(16, 16))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(color);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                trayIcon.Dispose();
                activityTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PreventLockApp());
        }
    }
}
