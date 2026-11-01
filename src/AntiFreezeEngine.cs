using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SmartMemReduct
{
    public class UnfreezeResult
    {
        public int ThrottledProcesses { get; set; }
        public bool DwmBoosted { get; set; }
        public CleanResult CleanStats { get; set; } = new CleanResult();
    }

    public static class AntiFreezeEngine
    {
        // Processes known to be CPU-intensive background workers.
        // These are safe to demote to BelowNormal without hurting system stability.
        private static readonly string[] CpuIntensiveProcessNames = new[]
        {
            "node",
            "python",
            "python3",
            "pythonw",
            "cline",
            "msbuild",
            "csc",
            "git",
            "vbc",
            "cargo",
            "rustc",
            "cl",
            "dotnet",
            "java",
            "javaw",
            "ruby",
            "perl",
            "go",
            "gcc",
            "g++",
            "make",
            "cmake",
            "npm",
            "yarn",
            "pnpm",
            "webpack",
            "vite",
            "tsc",
            "esbuild",
            "wsl"
        };

        /// <summary>
        /// Emergency Unfreeze:
        /// 1. Immediately prioritizes DWM and Explorer so the display and mouse unfreeze.
        /// 2. Sets runaway background AI/build processes to BelowNormal priority.
        /// 3. Performs a Deep SmartClean to relieve page thrashing.
        /// </summary>
        public static UnfreezeResult EmergencyUnfreeze()
        {
            var result = new UnfreezeResult();
            uint foregroundPid = MemoryEngine.GetForegroundProcessId();

            // 1. Boost DWM and Explorer priority immediately
            try
            {
                foreach (var p in Process.GetProcessesByName("dwm"))
                {
                    try
                    {
                        p.PriorityClass = ProcessPriorityClass.High;
                        result.DwmBoosted = true;
                    }
                    catch { }
                    finally { p.Dispose(); }
                }

                foreach (var p in Process.GetProcessesByName("explorer"))
                {
                    try
                    {
                        p.PriorityClass = ProcessPriorityClass.AboveNormal;
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }

            // 2. Throttle background CPU-heavy workers
            result.ThrottledProcesses = ThrottleBackgroundWorkers(foregroundPid);

            // 3. Deep purge memory pressure
            result.CleanStats = MemoryEngine.SmartClean(CleanLevel.Deep);

            // 4. Return DWM to Normal priority after 8 seconds to remain well-behaved
            Task.Run(async () =>
            {
                await Task.Delay(8000);
                try
                {
                    foreach (var p in Process.GetProcessesByName("dwm"))
                    {
                        try { p.PriorityClass = ProcessPriorityClass.Normal; }
                        catch { }
                        finally { p.Dispose(); }
                    }
                    foreach (var p in Process.GetProcessesByName("explorer"))
                    {
                        try { p.PriorityClass = ProcessPriorityClass.Normal; }
                        catch { }
                        finally { p.Dispose(); }
                    }
                }
                catch { }
            });

            return result;
        }

        /// <summary>
        /// Throttles background AI / worker processes to BelowNormal priority
        /// so they never starve the foreground app or Windows desktop.
        /// </summary>
        public static int ThrottleBackgroundWorkers(uint? explicitForegroundPid = null)
        {
            uint foregroundPid = explicitForegroundPid ?? MemoryEngine.GetForegroundProcessId();
            int throttled = 0;

            foreach (var name in CpuIntensiveProcessNames)
            {
                try
                {
                    Process[] procs = Process.GetProcessesByName(name);
                    foreach (var p in procs)
                    {
                        try
                        {
                            if (p.Id != foregroundPid &&
                                p.PriorityClass != ProcessPriorityClass.BelowNormal &&
                                p.PriorityClass != ProcessPriorityClass.Idle)
                            {
                                p.PriorityClass = ProcessPriorityClass.BelowNormal;
                                throttled++;
                            }
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                }
                catch { }
            }

            return throttled;
        }
    }
}
