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
            Console.WriteLine($"Starting RemoteCore, current user: {WindowsIdentity.GetCurrent().Name}");
            Console.WriteLine($"Is admin: {IsAdministrator()}");

            if (!IsAdministrator())
            {
                Console.WriteLine("Not running as admin, trying to elevate...");
                RunAsAdmin();
                return;
            }

            Console.WriteLine("Running as admin, checking installation...");
            if (InstallToProgramFiles())
            {
                return; // Exits if it successfully copied and restarted
            }

            Console.WriteLine("Starting main client...");
            bool createdNew;
            _mutex = new Mutex(true, "RemoteCore_Mutex_Unique_ID", out createdNew);

            if (!createdNew)
            {
                // App is already running, exit.
                Console.WriteLine("Another instance is already running, exiting...");
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
            Console.WriteLine($"Attempting to run as admin: {Environment.ProcessPath}");
            var processInfo = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            try
            {
                Process.Start(processInfo);
                Console.WriteLine("Admin process started successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error elevating: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                // 如果用户拒绝 UAC 提示，我们仍然可以尝试以普通用户身份运行
                Console.WriteLine("User declined UAC or failed to elevate, continuing with limited privileges...");
            }
        }

        static bool InstallToProgramFiles()
        {
            // Do not self-copy if running from visual studio or uncompiled
            if (!Environment.ProcessPath!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Not running as compiled EXE, skipping installation");
                return false;
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string targetDir = Path.Combine(programFiles, "RemoteCore");
            string targetPath = Path.Combine(targetDir, "RemoteCore.exe");
            string currentPath = Environment.ProcessPath;

            if (currentPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Already running from Program Files");
                return false; // Already running from target location
            }

            try
            {
                Console.WriteLine($"Installing to Program Files: {targetPath}");
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                if (File.Exists(targetPath))
                {
                    Console.WriteLine("Existing file found, trying to remove...");
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

                Console.WriteLine("Starting from Program Files...");
                // 确保以管理员权限重新启动
                var psi = new ProcessStartInfo(targetPath)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error installing to Program Files: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        internal static void SetAutoStart(bool enable)
        {
            try
            {
                // Clean up old registry-based autostart to prevent duplicate runs
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                    if (key != null && key.GetValue("RemoteCore") != null)
                    {
                        key.DeleteValue("RemoteCore", false);
                    }
                }
                catch { }

                string taskName = "RemoteCore_AutoStart";
                if (enable)
                {
                    string exePath = Environment.ProcessPath!;
                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/create /tn \"{taskName}\" /tr \"\\\"{exePath}\\\"\" /sc onlogon /rl highest /f",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    Process.Start(psi)?.WaitForExit();
                }
                else
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/delete /tn \"{taskName}\" /f",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    Process.Start(psi)?.WaitForExit();
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