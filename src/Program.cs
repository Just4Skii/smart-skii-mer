using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.Versioning;

namespace SmartMemReduct
{
    internal static class Program
    {
        private const string AppMutexName = @"SmartSkiimer_SingleInstance_Mutex";

        [STAThread]
        [SupportedOSPlatform("windows")]
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                LogFatal(e.ExceptionObject as Exception);
            };

            Application.ThreadException += (s, e) =>
            {
                LogFatal(e.Exception);
            };

            try
            {
                if (args.Any(a => string.Equals(a, "--enable-remote", StringComparison.OrdinalIgnoreCase)))
                {
                    RunExe("enable-remote.cmd");
                    return;
                }

                if (args.Any(a => string.Equals(a, "--install-enable-remote", StringComparison.OrdinalIgnoreCase)))
                {
                    EnableRemoteAtInstall();
                    System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sk-install-remote.ok"), DateTime.Now.ToString("O"));
                    return;
                }

                if (args.Any(a => string.Equals(a, "--selftest-remote", StringComparison.OrdinalIgnoreCase)))
                {
                    RunRemoteSelfTest();
                    return;
                }

                if (args.Any(a => string.Equals(a, "--allow-lan", StringComparison.OrdinalIgnoreCase)))
                {
                    AddUrlAcl();
                    return;
                }

                if (args.Length >= 2 && args[0].Equals("--set-nvidia-key", StringComparison.OrdinalIgnoreCase))
                {
                    LocalKeyStore.Save(args[1]);
                    return;
                }

                using (var mutex = new Mutex(true, AppMutexName, out bool createdNew))
                {
                    if (!createdNew)
                    {
                        MessageBox.Show(
                            "Smart Skii-mer is already running in your system tray!",
                            "Smart Skii-mer",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                        return;
                    }

                    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                    ApplicationConfiguration.Initialize();

                    // Allow the dashboard to be opened directly (shortcut, automation, demo).
                    if (args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "--ai", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase)))
                    {
                        var page = args.FirstOrDefault(a => a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase))?.Substring(7)
                        ?? (args.Any(a => string.Equals(a, "--ai", StringComparison.OrdinalIgnoreCase)) ? "ai" : "overview");
                        Application.Run(new DashboardForm(page));
                        return;
                    }

                    Application.Run(new TrayApplicationContext());
                }
            }
            catch (Exception ex)
            {
                LogFatal(ex);
            }
        }

        /// <summary>Reserves http://+:47821/ for the current user so the phone app can connect over Wi-Fi.</summary>

        /// <summary>Enables the phone remote at install time (called by the installer, already elevated).</summary>
        private static void EnableRemoteAtInstall()
        {
            try
            {
                var user = Environment.UserDomainName + "\\" + Environment.UserName;
                Run("netsh", $"http add urlacl url=http://+:{RemoteServer.Port}/ user={user} sdDL=D:(A;;GX;;;WD)");
                Run("netsh", $"advfirewall firewall delete rule name=\"Smart Skii-mer Remote\"");
                Run("netsh", $"advfirewall firewall add rule name=\"Smart Skii-mer Remote\" dir=in action=allow protocol=TCP localport={RemoteServer.Port} profile=private");
                StartWithWindows();
            }
            catch { }
        }

        private static void StartWithWindows()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                key?.SetValue("Smart Skii-mer", $"\"{Application.ExecutablePath}\" --tray");
            }
            catch { }
        }

        private static void RunExe(string name)
        {
            try
            {
                var path = System.IO.Path.Combine(AppContext.BaseDirectory, name);
                if (!System.IO.File.Exists(path)) return;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"{path}\"")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal,
                });
            }
            catch { }
        }

        private static void Run(string exe, string args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(15000);
        }

        private static void RunRemoteSelfTest()
        {
            using var server = new RemoteServer(_ => { }, () => "{\"ram\":42,\"cpu\":7,\"mode\":\"Balanced\",\"modeCmd\":\"mode_balance\",\"app\":\"self-test\",\"time\":\"00:00\"}");
            server.Start();
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skremote.txt"),
                $"running={server.Running} lan={server.LanAvailable} code={server.PairCode} err={server.LastError} urls={string.Join(",", RemoteServer.LocalAddresses())}");
        }

        private static void AddUrlAcl()
        {
            try
            {
                var user = Environment.UserDomainName + "\\" + Environment.UserName;
                var psi = new System.Diagnostics.ProcessStartInfo("netsh", $"http add urlacl url=http://+:{RemoteServer.Port}/ user={user}")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                };
                System.Diagnostics.Process.Start(psi)?.WaitForExit(8000);
            }
            catch { }
        }

        private static void LogFatal(Exception? ex)
        {
            if (ex == null) return;
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Smart Skii-mer"
                );
                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);

                string logPath = Path.Combine(logDir, "crash.log");
                File.AppendAllText(logPath, $"[{DateTime.Now}] {ex}\n\n");
                MessageBox.Show($"Startup Error: {ex.Message}\n\nDetails saved to:\n{logPath}", "Smart Skii-mer Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}