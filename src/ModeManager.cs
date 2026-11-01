using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartMemReduct
{
    public sealed class ModeProfile
    {
        public string Id { get; init; } = "balanced";
        public string Name { get; init; } = "Balanced";
        public string Description { get; init; } = "";
        public bool EcoBackground { get; init; } = true;
        public bool PauseCleaning { get; init; }
        public int EcoMinWorkingSetMB { get; init; } = 120;
        public int SampleSeconds { get; init; } = 3;
        public string VendorHint { get; init; } = "";
    }

    /// <summary>
    /// Mode policy. Modes change *how aggressively* background apps are throttled,
    /// never which apps are killed. Detection is advisory: the user can pin a mode.
    /// </summary>
    public static class ModeManager
    {
        public static readonly ModeProfile[] All =
        {
            new() { Id = "auto", Name = "Auto", Description = "Detect the mode from the app you are using", VendorHint = "" },
            new() { Id = "gaming", Name = "Gaming", Description = "Protect the game; throttle launchers, browsers and chat", PauseCleaning = false, EcoMinWorkingSetMB = 60, SampleSeconds = 5, VendorHint = "Set Armoury Crate / vendor app to Turbo and GPU mode Optimized or Ultimate." },
            new() { Id = "streaming", Name = "Streaming", Description = "Protect the encoder and game; limit everything else", PauseCleaning = false, EcoMinWorkingSetMB = 80, SampleSeconds = 5, VendorHint = "Keep the encoder on the GPU; close browser tabs you are not showing on stream." },
            new() { Id = "work", Name = "Work", Description = "Keep browser and office apps responsive; eco only idle apps", EcoMinWorkingSetMB = 250, SampleSeconds = 4, VendorHint = "Use Balanced power; keep the charger connected for sustained clocks." },
            new() { Id = "eco", Name = "Eco / Battery", Description = "Throttle almost everything in the background", EcoMinWorkingSetMB = 60, SampleSeconds = 8, VendorHint = "Use Silent/Balanced vendor profile and Windows Best power efficiency." },
            new() { Id = "balanced", Name = "Balanced", Description = "Default: clean up idle apps without touching what you use", EcoMinWorkingSetMB = 120 },
        };

        private static readonly string[] StreamingApps = { "obs", "streamlabs", "xsplit", "vmix", "twitch", "youtube", "discord" };
        private static readonly string[] WorkApps = { "excel", "word", "powerpnt", "winword", "code", "devenv", "rider", "idea", "teams", "slack", "zoom", "outlook" };

        public static ModeProfile Get(string id) =>
            All.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? All[^1];

        public static ModeProfile Resolve(string configured, SystemSnapshot? snapshot)
        {
            if (!configured.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return Get(configured);
            if (snapshot is null) return Get("balanced");

            var fg = snapshot.ForegroundName.ToLowerInvariant();
            if (snapshot.ForegroundFullscreenGame || snapshot.Processes.Any(p => p.Foreground && p.GameLikely))
                return Get("gaming");
            if (StreamingApps.Any(fg.Contains) || snapshot.ForegroundFullscreenGame)
                return Get("streaming");
            if (WorkApps.Any(fg.Contains))
                return Get("work");
            if (!IsPluggedIn())
                return Get("eco");
            return Get("balanced");
        }

        private static bool IsPluggedIn()
        {
            try
            {
                var status = System.Windows.Forms.SystemInformation.PowerStatus;
                return status.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
            }
            catch { return true; }
        }

        public static bool ShouldEco(ModeProfile mode) => mode.EcoBackground;

        /// <summary>RAM % at which the assistant may act on its own in each mode.</summary>
        public static int AssistanceThreshold(ModeProfile mode) => mode.Id switch
        {
            "gaming" => 85,
            "streaming" => 85,
            "eco" => 80,
            _ => 90,
        };
    }
}
