using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SmartMemReduct
{
    public sealed class ProcSample
    {
        public string Name { get; init; } = "";
        public int Pid { get; init; }
        public double WorkingSetMB { get; init; }
        public double PrivateMB { get; init; }
        public double CpuPercent { get; set; }
        public string Title { get; init; } = "";
        public bool Foreground { get; init; }
        public bool Protected { get; init; }
        public bool GameLikely { get; init; }
    }

    public sealed class SystemSnapshot
    {
        public MemoryInfo Memory { get; init; } = new();
        public double CpuPercent { get; init; }
        public List<ProcSample> Processes { get; init; } = new();
        public string ForegroundName { get; init; } = "";
        public int ForegroundPid { get; init; }
        public bool ForegroundFullscreenGame { get; init; }
        public string Manufacturer { get; init; } = "";
        public string CpuName { get; init; } = "";
        public bool IsLaptop { get; init; }
        public string WindowsVersion { get; init; } = "";
        public string Model { get; init; } = "";
        public string Gpu { get; init; } = "";
        public string GpuDriver { get; init; } = "";
        public string PowerPlan { get; init; } = "";
        public List<string> VendorServices { get; init; } = new();
        public DateTime CapturedAt { get; init; } = DateTime.Now;
    }

    public static class SystemProbe
    {
        private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "system", "registry", "memory compression", "smss", "csrss", "wininit", "winlogon",
            "services", "lsass", "svchost", "dwm", "explorer", "fontdrvhost", "sihost",
            "ctfmon", "audiodg", "spoolsv", "taskhostw", "searchhost", "startmenuexperiencehost",
            "shellexperiencehost", "runtimebroker", "applicationframehost", "smart skii-mer",
            "idle", "secure system", "wudfhost", "dllhost", "conhost",
        };

        private static readonly string[] GameMarkers =
        {
            "steam", "epicgameslauncher", "riot", "valorant", "cs2", "fortnite", "gta", "fivem",
            "eldenring", "cyberpunk", "minecraft", "roblox", "warzone", "cod", "fifa", "fc24",
            "forza", "genshin", "league", "dota", "overwatch", "apex", "pubg", "rust", "destiny",
        };

        private static bool IsProtected(string name) =>
            ProtectedNames.Contains(name) || name.StartsWith("smart skii", StringComparison.OrdinalIgnoreCase);

        public static bool LooksLikeGame(string name, string title)
        {
            var s = (name + " " + title).ToLowerInvariant();
            return GameMarkers.Any(s.Contains);
        }

        public static async Task<SystemSnapshot> CaptureAsync(bool includeCpu, CancellationToken ct = default)
        {
            var mem = MemoryEngine.GetCurrentMemoryInfo();
            var fgPid = (int)MemoryEngine.GetForegroundProcessId();
            var samples = new List<ProcSample>();
            double cpu = 0;

            var first = new Dictionary<int, TimeSpan>();
            var started = DateTime.UtcNow;
            var cores = Math.Max(1, Environment.ProcessorCount);

            var procs = Process.GetProcesses();
            foreach (var p in procs)
            {
                try
                {
                    var name = p.ProcessName;
                    var title = SafeTitle(p);
                    var pid = p.Id;
                    var ws = p.WorkingSet64 / 1048576.0;
                    var priv = p.PrivateMemorySize64 / 1048576.0;
                    first[pid] = SafeCpu(p);
                    samples.Add(new ProcSample
                    {
                        Name = name,
                        Pid = pid,
                        WorkingSetMB = Math.Round(ws, 1),
                        PrivateMB = Math.Round(priv, 1),
                        Title = title,
                        Foreground = pid == fgPid,
                        Protected = IsProtected(name),
                        GameLikely = LooksLikeGame(name, title),
                    });
                }
                catch { }
                finally { p.Dispose(); }
            }

            if (includeCpu)
            {
                await Task.Delay(320, ct).ConfigureAwait(false);
                var elapsed = (DateTime.UtcNow - started).TotalMilliseconds;
                if (elapsed > 50)
                {
                    foreach (var s in samples)
                    {
                        try
                        {
                            using var p = Process.GetProcessById(s.Pid);
                            var now = SafeCpu(p);
                            if (first.TryGetValue(s.Pid, out var before))
                                s.CpuPercent = Math.Round(Math.Max(0, (now - before).TotalMilliseconds) / (elapsed * cores) * 100, 1);
                        }
                        catch { }
                    }
                }
            }

            var ordered = samples.OrderByDescending(s => s.WorkingSetMB).Take(40).ToList();
            var fg = samples.FirstOrDefault(s => s.Foreground);

            return new SystemSnapshot
            {
                Memory = mem,
                CpuPercent = Math.Round(ordered.Sum(s => s.CpuPercent), 1),
                Processes = ordered,
                ForegroundName = fg?.Name ?? "unknown",
                ForegroundPid = fgPid,
                ForegroundFullscreenGame = fg is not null && IsFullscreen(fgPid) && !IsNonGameFullscreen(fg.Name),
                Manufacturer = ReadBios("SystemManufacturer"),
                CpuName = ReadCpu(),
                IsLaptop = IsLaptopMachine(),
                WindowsVersion = Environment.OSVersion.VersionString,
                Model = ReadBios("SystemProductName"),
                Gpu = ReadGpu(out var driver),
                GpuDriver = driver,
                PowerPlan = ActivePowerPlan(),
                VendorServices = VendorServices(),
            };
        }

        private static string SafeTitle(Process p)
        {
            try { return p.MainWindowTitle ?? ""; } catch { return ""; }
        }

        private static TimeSpan SafeCpu(Process p)
        {
            try { return p.TotalProcessorTime; } catch { return TimeSpan.Zero; }
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private static bool IsNonGameFullscreen(string name)
        {
            string[] nongame = { "chrome", "msedge", "brave", "firefox", "opera", "explorer", "code", "devenv", "rider", "idea", "teams", "zoom", "discord", "vlc", "mpv", "obs64", "streamlabs", "powershell", "windowsterminal", "smart skii" };
            var n = name.ToLowerInvariant();
            return nongame.Any(n.Contains);
        }

        private static bool IsFullscreen(int pid)
        {
            try
            {
                var h = NativeMethods.GetForegroundWindow();
                if (h == IntPtr.Zero) return false;
                if (!GetWindowRect(h, out var r)) return false;
                var screen = System.Windows.Forms.Screen.FromHandle(h).Bounds;
                var left = Math.Max(r.Left, screen.Left);
                var top = Math.Max(r.Top, screen.Top);
                var right = Math.Min(r.Right, screen.Right);
                var bottom = Math.Min(r.Bottom, screen.Bottom);
                if (right <= left || bottom <= top) return false;
                var covered = (double)(right - left) * (bottom - top);
                var total = (double)screen.Width * screen.Height;
                return covered / total >= 0.9;
            }
            catch { return false; }
        }

        private static string ReadCpu()
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return (k?.GetValue("ProcessorNameString") as string)?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private static bool IsLaptopMachine()
        {
            try
            {
                var status = System.Windows.Forms.SystemInformation.PowerStatus;
                return status.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery;
            }
            catch { return false; }
        }

        private static string ReadBios(string name)
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                return (k?.GetValue(name) as string)?.Trim() ?? "";
            }
            catch { return ""; }
        }

        private static string ReadGpu(out string driver)
        {
            driver = "";
            try
            {
                using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (cls is null) return "";
                foreach (var sub in cls.GetSubKeyNames())
                {
                    if (!int.TryParse(sub, out _)) continue;
                    using var k = cls.OpenSubKey(sub);
                    var desc = k?.GetValue("DriverDesc") as string;
                    if (string.IsNullOrWhiteSpace(desc)) continue;
                    driver = (k?.GetValue("DriverVersion") as string) ?? "";
                    return desc.Trim();
                }
            }
            catch { }
            return "";
        }

        private static string ActivePowerPlan()
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", "/getactivescheme")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p is null) return "";
                var text = p.StandardOutput.ReadToEnd();
                p.WaitForExit(1500);
                var idx = text.IndexOf("(", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var end = text.IndexOf(")", idx, StringComparison.Ordinal);
                    if (end > idx) return text[(idx + 1)..end].Trim();
                }
            }
            catch { }
            return "";
        }

        private static List<string> VendorServices()
        {
            var hits = new List<string>();
            try
            {
                using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
                if (services is null) return hits;
                string[] markers = { "asus", "armoury", "armourycrate", "msi", "omen", "alienware", "razer", "legion", "lenovo", "predator", "nitro", "gigabyte", "aorus" };
                foreach (var name in services.GetSubKeyNames())
                {
                    var lower = name.ToLowerInvariant();
                    if (markers.Any(lower.Contains)) hits.Add(name);
                    if (hits.Count >= 25) break;
                }
            }
            catch { }
            return hits;
        }

        // ── Safe trim: working-set only, never kills, never touches foreground/protected ──
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        public static (int trimmed, double freedMb, List<string> names) TrimBackground(
            IEnumerable<ProcSample> candidates, double minMb = 80)
        {
            var names = new List<string>();
            double before = 0, after = 0;
            int count = 0;
            foreach (var c in candidates)
            {
                if (c.Protected || c.Foreground || c.GameLikely) continue;
                if (c.WorkingSetMB < minMb) continue;
                var h = OpenProcess(0x0100 | 0x1000, false, c.Pid); // SET_QUOTA | QUERY_LIMITED
                if (h == IntPtr.Zero) continue;
                try
                {
                    before += c.WorkingSetMB;
                    if (NativeMethods.EmptyWorkingSet(h))
                    {
                        using var p = Process.GetProcessById(c.Pid);
                        var ws = p.WorkingSet64 / 1048576.0;
                        after += ws;
                        names.Add(c.Name);
                        count++;
                    }
                }
                catch { }
                finally { CloseHandle(h); }
            }
            return (count, Math.Max(0, Math.Round(before - after, 1)), names);
        }


        // ── EcoQoS: the mechanism behind Windows "Efficiency mode" ──────────
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PROCESS_POWER_THROTTLING_STATE state, int size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetPriorityClass(IntPtr process, uint priorityClass);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        private const int ProcessPowerThrottling = 4;
        private const uint ThrottleExecutionSpeed = 0x1;
        private const uint IdlePriority = 0x00000040;
        private const uint NormalPriority = 0x00000020;

        public static bool SetEco(ProcSample p, bool enable)
        {
            if (p.Protected || p.Foreground || p.GameLikely || p.Name.StartsWith("smart skii", StringComparison.OrdinalIgnoreCase))
                return false;
            var h = OpenProcess(0x0200 | 0x1000, false, p.Pid); // SET_INFORMATION | QUERY_LIMITED
            if (h == IntPtr.Zero) return false;
            try
            {
                var state = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = 1,
                    ControlMask = ThrottleExecutionSpeed,
                    StateMask = enable ? ThrottleExecutionSpeed : 0,
                };
                var ok = SetProcessInformation(h, ProcessPowerThrottling, ref state, Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
                SetPriorityClass(h, enable ? IdlePriority : NormalPriority);
                return ok;
            }
            catch { return false; }
            finally { CloseHandle(h); }
        }

        /// <summary>
        /// Applies EcoQoS to idle background processes only. Foreground, games and
        /// protected system processes are always skipped. Reversible with enable=false.
        /// </summary>
        public static int ApplyEcoToBackground(IEnumerable<ProcSample> processes, int minMb, bool enable)
        {
            int changed = 0;
            foreach (var p in processes)
            {
                if (p.Protected || p.Foreground || p.GameLikely || GameFocus.IsRelated(p.Name)) continue;
                if (p.WorkingSetMB < minMb && enable) continue;
                if (SetEco(p, enable)) changed++;
            }
            return changed;
        }

        public static string ToCompactJson(SystemSnapshot s)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append($"\"ram_used_gb\":{s.Memory.UsedPhysicalGB},\"ram_total_gb\":{s.Memory.TotalPhysicalGB},");
            sb.Append($"\"commit_pct\":{s.Memory.CommitLoadPercent},\"cpu_pct\":{s.CpuPercent},");
            sb.Append($"\"foreground\":\"{Esc(s.ForegroundName)}\",\"fullscreen_game\":{s.ForegroundFullscreenGame.ToString().ToLower()},");
            sb.Append($"\"vendor\":\"{Esc(s.Manufacturer)} {Esc(s.Model)}\",\"gpu\":\"{Esc(s.Gpu)}\",\"gpu_driver\":\"{Esc(s.GpuDriver)}\",");
            sb.Append($"\"power_plan\":\"{Esc(s.PowerPlan)}\",\"vendor_services\":[{string.Join(",", s.VendorServices.Take(10).Select(v => $"\"{Esc(v)}\""))}],");
            sb.Append("\"processes\":[");
            sb.Append(string.Join(",", s.Processes.Take(25).Select(p =>
                $"{{\"name\":\"{Esc(p.Name)}\",\"pid\":{p.Pid},\"ram_mb\":{p.WorkingSetMB},\"cpu_pct\":{p.CpuPercent},\"foreground\":{p.Foreground.ToString().ToLower()},\"game\":{p.GameLikely.ToString().ToLower()}}}")));
            sb.Append("]}");
            return sb.ToString();
        }


        public static string ToAiJson(SystemSnapshot s)
        {
            var sb = new StringBuilder();
            sb.Append("{\"ram_gb\":\"" + s.Memory.UsedPhysicalGB.ToString("0.0") + "/" + s.Memory.TotalPhysicalGB.ToString("0.0") + "\",");
            sb.Append("\"commit\":" + s.Memory.CommitLoadPercent + ",\"cpu\":" + Math.Round(s.CpuPercent) + ",");
            sb.Append("\"fg\":\"" + Esc(s.ForegroundName) + "\",\"gpu\":\"" + Esc(s.Gpu) + "\",\"power\":\"" + Esc(s.PowerPlan) + "\",");
            sb.Append("\"vendor\":\"" + Esc(s.Manufacturer + " " + s.Model).Trim() + "\",");
            sb.Append("\"top\":[");
            sb.Append(string.Join(",", s.Processes.Take(4).Select(pr => "{\"n\":\"" + Esc(pr.Name) + "\",\"mb\":" + Math.Round(pr.WorkingSetMB) + "}")));
            sb.Append("]}");
            return sb.ToString();
        }

        private static string Esc(string v) => v.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
