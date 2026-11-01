using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SmartMemReduct
{
    public class MemoryInfo
    {
        public ulong TotalPhysicalBytes { get; set; }
        public ulong AvailablePhysicalBytes { get; set; }
        public ulong UsedPhysicalBytes => TotalPhysicalBytes > AvailablePhysicalBytes ? TotalPhysicalBytes - AvailablePhysicalBytes : 0;
        public int MemoryLoadPercent { get; set; }

        public ulong TotalCommitBytes { get; set; }
        public ulong AvailableCommitBytes { get; set; }
        public ulong UsedCommitBytes => TotalCommitBytes > AvailableCommitBytes ? TotalCommitBytes - AvailableCommitBytes : 0;
        public int CommitLoadPercent => TotalCommitBytes > 0 ? (int)((UsedCommitBytes * 100) / TotalCommitBytes) : 0;

        public double TotalPhysicalGB => Math.Round(TotalPhysicalBytes / (1024.0 * 1024.0 * 1024.0), 2);
        public double AvailablePhysicalGB => Math.Round(AvailablePhysicalBytes / (1024.0 * 1024.0 * 1024.0), 2);
        public double UsedPhysicalGB => Math.Round(UsedPhysicalBytes / (1024.0 * 1024.0 * 1024.0), 2);

        public double TotalCommitGB => Math.Round(TotalCommitBytes / (1024.0 * 1024.0 * 1024.0), 2);
        public double UsedCommitGB => Math.Round(UsedCommitBytes / (1024.0 * 1024.0 * 1024.0), 2);
    }

    public class CleanResult
    {
        public double FreedMB { get; set; }
        public int BeforePercent { get; set; }
        public int AfterPercent { get; set; }
        public int ProcessesTrimmed { get; set; }
        public bool StandbyPurged { get; set; }
        public bool ModifiedFlushed { get; set; }
        public CleanLevel Level { get; set; }
    }

    public enum CleanLevel
    {
        /// <summary>Only purge standby list. No working-set trimming. Zero risk.</summary>
        Light,
        /// <summary>Purge standby + modified lists. Trim only bloated idle background processes.</summary>
        Standard,
        /// <summary>Full purge + trim all non-protected background processes. Most aggressive safe mode.</summary>
        Deep
    }

    public static class MemoryEngine
    {
        // Processes that must NEVER be trimmed to avoid system instability or blue screen.
        // This is the critical safety list.
        private static readonly HashSet<string> ProtectedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Windows display and shell — trimming these causes screen freezes
            "dwm",
            "explorer",
            // Windows kernel and session critical — trimming these risks BSOD
            "csrss",
            "lsass",
            "services",
            "smss",
            "winlogon",
            "system",
            "wininit",
            "svchost",        // Hosts hundreds of critical Windows services
            "runtimebroker",  // Windows app permission broker
            "sihost",         // Shell Infrastructure Host
            "fontdrvhost",    // Font rendering (trim = font corruption/crash)
            "conhost",        // Console window host
            "wudfhost",       // Windows driver framework
            "dashost",        // Device Association Service
            "ctfmon",         // Text input services
            // Audio — trimming causes audio glitches/pops
            "audiodg",
            "audioservice",
            // Security and antivirus
            "msmpeng",        // Windows Defender
            "mssense",        // Microsoft Defender ATP
            "securityhealthservice",
            "smartscreen",
            // Drivers and hardware
            "nvcontainer",    // NVIDIA driver container
            "nvdisplay.container",
            "amdrsserv",      // AMD driver
            "igfxcuiservice", // Intel graphics
            // Self
            "smartmemreduct",
            "smart skii-mer",
            "smartskiimer"
        };

        // Processes we should never trim even in Deep mode because the user
        // explicitly runs them (AI tasks etc.)
        private static readonly HashSet<string> UserProtectedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "antigravity",
            "deltaforce",     // The user's game — never touch
            "delta_force"
        };

        static MemoryEngine()
        {
            NativeMethods.EnableAllRequiredPrivileges();
        }

        /// <summary>
        /// Compacts memory and trims Smart Skii-mer's own working set down to 3-6 MB.
        /// </summary>
        public static void TrimSelfWorkingSet()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                NativeMethods.EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch { }
        }

        public static double GetSelfWorkingSetMB()
        {
            try
            {
                using var p = Process.GetCurrentProcess();
                return Math.Round(p.WorkingSet64 / (1024.0 * 1024.0), 1);
            }
            catch { return 0; }
        }

        // ── Memory info ──────────────────────────────────────────────

        public static MemoryInfo GetCurrentMemoryInfo()
        {
            var status = new NativeMethods.MEMORYSTATUSEX();
            if (NativeMethods.GlobalMemoryStatusEx(status))
            {
                return new MemoryInfo
                {
                    TotalPhysicalBytes = status.ullTotalPhys,
                    AvailablePhysicalBytes = status.ullAvailPhys,
                    MemoryLoadPercent = (int)status.dwMemoryLoad,
                    TotalCommitBytes = status.ullTotalPageFile,
                    AvailableCommitBytes = status.ullAvailPageFile
                };
            }
            return new MemoryInfo();
        }

        // ── Kernel-level memory list operations ──────────────────────

        /// <summary>
        /// Purge standby memory lists back to Free list.
        /// This is the safest and most effective single operation — it reclaims
        /// cached file data that Windows keeps "just in case" but doesn't need.
        /// NO processes are affected. NO data is lost. Zero disk I/O.
        /// </summary>
        public static bool PurgeStandbyList()
        {
            try
            {
                int cmd = NativeMethods.MemoryPurgeStandbyList;
                int r1 = NativeMethods.NtSetSystemInformation(NativeMethods.SystemMemoryListInformation, ref cmd, sizeof(int));

                cmd = NativeMethods.MemoryPurgeLowPriorityStandbyList;
                int r2 = NativeMethods.NtSetSystemInformation(NativeMethods.SystemMemoryListInformation, ref cmd, sizeof(int));

                return r1 == 0 || r2 == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Flush modified page list — tells the memory manager to write dirty pages
        /// to their backing store (pagefile) and move them to standby/free.
        /// This is safe but causes a small burst of disk I/O.
        /// </summary>
        public static bool FlushModifiedPageList()
        {
            try
            {
                int cmd = NativeMethods.MemoryFlushModifiedList;
                int r = NativeMethods.NtSetSystemInformation(NativeMethods.SystemMemoryListInformation, ref cmd, sizeof(int));
                return r == 0;
            }
            catch
            {
                return false;
            }
        }

        // ── Foreground / fullscreen detection ────────────────────────

        public static uint GetForegroundProcessId()
        {
            try
            {
                IntPtr hwnd = NativeMethods.GetForegroundWindow();
                if (hwnd != IntPtr.Zero)
                {
                    NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                    return pid;
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Checks if the foreground window is a fullscreen application (game, video player, etc.)
        /// by testing if the window covers the entire primary screen.
        /// </summary>
        public static bool IsFullscreenAppActive()
        {
            try
            {
                IntPtr hwnd = NativeMethods.GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                    return false;

                var screen = Screen.PrimaryScreen;
                if (screen == null)
                    return false;

                var rect = new System.Drawing.Rectangle();
                if (GetWindowRect(hwnd, ref rect))
                {
                    return rect.Left <= screen.Bounds.Left &&
                           rect.Top <= screen.Bounds.Top &&
                           rect.Right >= screen.Bounds.Right &&
                           rect.Bottom >= screen.Bounds.Bottom;
                }
            }
            catch { }
            return false;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, ref System.Drawing.Rectangle lpRect);

        // ── Process classification ───────────────────────────────────

        private static bool IsProtectedProcess(Process p, uint foregroundPid, int selfPid)
        {
            if (p.Id == selfPid || (uint)p.Id == foregroundPid)
                return true;

            string name = p.ProcessName;

            if (ProtectedProcessNames.Contains(name))
                return true;

            if (UserProtectedProcessNames.Contains(name))
                return true;

            return false;
        }

        /// <summary>
        /// Returns true if the process appears to have a visible window on screen
        /// (i.e. it is a user-facing application, not a background worker).
        /// </summary>
        private static bool HasVisibleWindow(Process p)
        {
            try
            {
                return p.MainWindowHandle != IntPtr.Zero &&
                       NativeMethods.IsWindowVisible(p.MainWindowHandle) &&
                       !NativeMethods.IsIconic(p.MainWindowHandle);
            }
            catch
            {
                return false;
            }
        }

        // ── Smart Clean ──────────────────────────────────────────────

        /// <summary>
        /// The main cleaning operation with three levels of aggressiveness.
        ///
        /// Light:    Purge standby list only. Zero risk, zero process interference.
        ///           Best for frequent automatic cleaning.
        ///
        /// Standard: Purge standby + modified lists. Trim only bloated background
        ///           processes whose working set exceeds the configured threshold
        ///           AND that have no visible/active window.
        ///           This is the default for manual Ctrl+Alt+M and auto-clean.
        ///
        /// Deep:     Full purge + trim ALL non-protected background processes.
        ///           Use when RAM is critically high (>90%).
        ///           Still NEVER touches DWM, explorer, csrss, foreground app, etc.
        /// </summary>
        public static CleanResult SmartClean(CleanLevel level = CleanLevel.Standard)
        {
            var before = GetCurrentMemoryInfo();
            uint foregroundPid = GetForegroundProcessId();
            int currentPid = Environment.ProcessId;

            bool standby = false;
            bool modified = false;

            // ── Stage 1: Always purge standby list (safe for all levels) ──
            if (SettingsManager.Current.PurgeStandbyList || level != CleanLevel.Light)
            {
                standby = PurgeStandbyList();
            }

            // ── Stage 2: Flush modified list (Standard + Deep) ──
            if (level >= CleanLevel.Standard && SettingsManager.Current.FlushModifiedList)
            {
                modified = FlushModifiedPageList();
            }

            // ── Stage 3: Working set trimming (Standard + Deep) ──
            int trimmed = 0;
            if (level >= CleanLevel.Standard && SettingsManager.Current.TrimBackgroundWorkingSets)
            {
                long minWorkingSetBytes = (long)SettingsManager.Current.TrimMinWorkingSetMB * 1024L * 1024L;

                Process[] processes;
                try { processes = Process.GetProcesses(); }
                catch { processes = Array.Empty<Process>(); }

                foreach (var p in processes)
                {
                    try
                    {
                        // Never touch protected or foreground processes
                        if (IsProtectedProcess(p, foregroundPid, currentPid))
                            continue;

                        // In Standard mode, only trim processes that are:
                        //  - Background (no visible, non-minimized window)
                        //  - Bloated (working set > threshold)
                        if (level == CleanLevel.Standard)
                        {
                            if (HasVisibleWindow(p))
                                continue;

                            long ws = 0;
                            try { ws = p.WorkingSet64; } catch { continue; }
                            if (ws < minWorkingSetBytes)
                                continue;
                        }

                        // In Deep mode, trim everything except protected processes.
                        // Still skip processes with visible windows in the foreground.
                        if (level == CleanLevel.Deep)
                        {
                            if (HasVisibleWindow(p))
                                continue;
                        }

                        if (NativeMethods.EmptyWorkingSet(p.Handle))
                        {
                            trimmed++;
                        }
                    }
                    catch
                    {
                        // Some system processes reject access; safely ignore
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }

            var after = GetCurrentMemoryInfo();
            double freedMB = 0;
            if (after.AvailablePhysicalBytes > before.AvailablePhysicalBytes)
            {
                freedMB = Math.Round((after.AvailablePhysicalBytes - before.AvailablePhysicalBytes) / (1024.0 * 1024.0), 1);
            }

            // Trim Smart Skii-mer itself so it stays featherlight (3-6 MB)
            TrimSelfWorkingSet();

            return new CleanResult
            {
                FreedMB = freedMB,
                BeforePercent = before.MemoryLoadPercent,
                AfterPercent = after.MemoryLoadPercent,
                ProcessesTrimmed = trimmed,
                StandbyPurged = standby,
                ModifiedFlushed = modified,
                Level = level
            };
        }

        /// <summary>
        /// Determines the appropriate clean level based on current memory pressure.
        /// </summary>
        public static CleanLevel RecommendCleanLevel()
        {
            var mem = GetCurrentMemoryInfo();
            if (mem.MemoryLoadPercent >= 90)
                return CleanLevel.Deep;
            if (mem.MemoryLoadPercent >= 75)
                return CleanLevel.Standard;
            return CleanLevel.Light;
        }
    }
}
