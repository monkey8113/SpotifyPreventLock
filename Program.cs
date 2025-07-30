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
    public class PreventLockApp : ApplicationContext
    {
        // [Keep all existing DllImports, enums, and fields...]

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
                        // Request admin rights if needed
                        if (MessageBox.Show("This requires administrator permissions. Elevate now?", 
                            "Permission Needed", MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            RestartAsAdmin();
                            return;
                        }
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
                        UseShellExecute = true
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
                        UseShellExecute = true
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

        private void UpdateStartupMenuItem(ToolStripMenuItem item)
        {
            bool isEnabled = IsInStartup();
            item.Checked = isEnabled;
            item.Text = isEnabled ? "✓ Start with Windows" : "Start with Windows";
        }

        // [Keep all other existing methods...]
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
