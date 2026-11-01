using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartMemReduct
{
    /// <summary>
    /// Deterministic fallback analysis. Produces the same shape as the AI report so the
    /// advisor remains useful with zero credits, no network, or a revoked key.
    /// </summary>
    public static class LocalAdvisor
    {
        public static AiReport Analyze(SystemSnapshot s, string? apiError = null)
        {
            var r = new AiReport { Ok = true, Provider = "Local analysis", Model = "rules", Health = Health(s) };
            var pct = s.Memory.TotalPhysicalGB > 0 ? s.Memory.UsedPhysicalGB / s.Memory.TotalPhysicalGB * 100 : 0;

            r.Summary = $"{s.Memory.UsedPhysicalGB:0.0} of {s.Memory.TotalPhysicalGB:0.0} GB in use ({pct:0}%), {s.Memory.CommitLoadPercent}% reserved, processor at {s.CpuPercent:0}%. " +
                        (s.ForegroundFullscreenGame
                            ? $"{s.ForegroundName} is fullscreen and protected; only background apps are eligible."
                            : $"{s.ForegroundName} is focused and is excluded from trims.");

            if (!string.IsNullOrWhiteSpace(apiError))
                r.Summary += " The AI service is unavailable, so this advice comes from the built-in checker.";

            if (pct >= 85)
            {
                r.Suggestions.Add(new AiSuggestion
                {
                    Title = "Memory pressure is high",
                    Detail = $"RAM is {pct:0}%. Largest background apps: {TopBackground(s, 3)}.",
                    Impact = "Freeing 0.5–2 GB can stop paging stalls",
                    Risk = "low",
                    Action = "trim_background",
                });
            }
            else if (pct >= 70)
            {
                r.Suggestions.Add(new AiSuggestion
                {
                    Title = "Trim idle background apps",
                    Detail = $"RAM is {pct:0}%. Candidates: {TopBackground(s, 3)}.",
                    Impact = "A few hundred MB without touching the focused app",
                    Risk = "low",
                    Action = "trim_background",
                });
            }

            if (s.VendorServices.Any(v => v.Contains("armoury", StringComparison.OrdinalIgnoreCase) || v.Contains("asus", StringComparison.OrdinalIgnoreCase)))
            {
                r.Suggestions.Add(new AiSuggestion
                {
                    Title = "Use the ASUS performance profile",
                    Detail = "Armoury Crate services are present. Choose Silent for battery, Balanced for desktop, Turbo for gaming; switch GPU mode to Optimized unless you need maximum FPS.",
                    Impact = "Better fan/GPU behaviour under load",
                    Risk = "low",
                    Action = "open_vendor_app",
                });
            }

            if (s.PowerPlan.Contains("Balanced", StringComparison.OrdinalIgnoreCase) && (s.ForegroundFullscreenGame || s.CpuPercent > 60))
            {
                r.Suggestions.Add(new AiSuggestion
                {
                    Title = "Raise the power plan while gaming",
                    Detail = $"Active plan is {s.PowerPlan}. Use High performance or the vendor's Turbo profile during gameplay, then return to Balanced afterwards.",
                    Impact = "Fewer frame-time dips",
                    Risk = "low",
                    Action = "open_power_settings",
                });
            }

            var gpuBound = s.Processes.Where(p => p.Name.Contains("chrome", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("msedge", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("brave", StringComparison.OrdinalIgnoreCase)).Sum(p => p.WorkingSetMB);
            if (gpuBound > 1200)
            {
                r.Suggestions.Add(new AiSuggestion
                {
                    Title = "Many browser processes are resident",
                    Detail = $"Browser processes total {gpuBound:0} MB. Close unused tabs, or enable the browser's memory saver mode.",
                    Impact = "Recovers several hundred MB",
                    Risk = "low",
                    Action = "none",
                });
            }

            if (r.Suggestions.Count == 0)
            {
                r.Suggestions.Add(new AiSuggestion
                {
                    Title = "System looks healthy",
                    Detail = "No pressure signals crossed the thresholds. Avoid aggressive cleaning; it can hurt cache hit rates.",
                    Impact = "No action needed",
                    Risk = "low",
                    Action = "none",
                });
            }

            return r;
        }

        private static string Health(SystemSnapshot s)
        {
            var pct = s.Memory.TotalPhysicalGB > 0 ? s.Memory.UsedPhysicalGB / s.Memory.TotalPhysicalGB * 100 : 0;
            return pct >= 85 ? "critical" : pct >= 70 ? "strained" : "healthy";
        }

        private static string TopBackground(SystemSnapshot s, int n) =>
            string.Join(", ", s.Processes
                .Where(p => !p.Protected && !p.Foreground)
                .OrderByDescending(p => p.WorkingSetMB)
                .Take(n)
                .Select(p => $"{p.Name} {p.WorkingSetMB:0} MB"));
    }
}
