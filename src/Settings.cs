using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace SmartMemReduct
{
    // ── AI settings ──

    public class AiSettings
    {
        public bool Enabled { get; set; } = false;
        public string ProviderName { get; set; } = "OpenRouter";
        public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
        public string ApiKey { get; set; } = "";
        public string ModelName { get; set; } = "google/gemini-2.5-flash";
        /// <summary>openrouter | nvidia | gemini | compat | auto</summary>
        public string Kind { get; set; } = "auto";
        /// <summary>Custom OpenAI-compatible base URL (e.g. https://api.deepseek.com/v1)</summary>
        public string CustomBaseUrl { get; set; } = "";
        /// <summary>Auto-analyze interval in minutes (0 = disabled)</summary>
        public int AutoAnalyzeIntervalMinutes { get; set; } = 0;
    }

    // ── Main settings ──

    public class AppSettings
    {
        // --- Auto-clean ---
        public bool AutoCleanEnabled { get; set; } = true;
        /// <summary>RAM usage % threshold to trigger auto-clean (default 75)</summary>
        public int AutoCleanThresholdPercent { get; set; } = 75;
        /// <summary>Minimum seconds between auto-cleans to prevent over-cleaning (default 30)</summary>
        public int AutoCleanCooldownSeconds { get; set; } = 30;
        /// <summary>How often (seconds) the auto-clean timer checks memory pressure (default 5)</summary>
        public int AutoCleanCheckIntervalSeconds { get; set; } = 5;

        // --- Modes / EcoQoS ---
        /// <summary>auto | gaming | streaming | work | eco | balanced</summary>
        public string Mode { get; set; } = "auto";
        /// <summary>Force background apps into Windows Efficiency mode (EcoQoS).</summary>
        public bool AutoEco { get; set; } = true;
        /// <summary>Let the assistant act automatically when memory is critical during gaming/streaming.</summary>
        public bool AutoAiOnPressure { get; set; } = true;
        /// <summary>User-defined deep-clean hotkey, e.g. Ctrl+Alt+D.</summary>
        public string DeepCleanHotkey { get; set; } = "Ctrl+Alt+D";
        public int EcoMinWorkingSetMB { get; set; } = 120;

        // --- Cleaning behaviour ---
        public bool PurgeStandbyList { get; set; } = true;
        public bool FlushModifiedList { get; set; } = true;
        public bool TrimBackgroundWorkingSets { get; set; } = true;
        /// <summary>Only trim processes whose working set exceeds this MB threshold (default 50)</summary>
        public int TrimMinWorkingSetMB { get; set; } = 50;

        // --- UI ---
        public bool StartWithWindows { get; set; } = true;
        public bool NotificationsEnabled { get; set; } = false;
        /// <summary>Closing the window keeps the app running in the notification area.</summary>
        public bool RunInBackground { get; set; } = true;
        /// <summary>Only show balloon when freed amount exceeds this MB (reduces notification spam)</summary>
        public int NotifyMinFreedMB { get; set; } = 20;

        // --- Safety ---
        /// <summary>Never auto-clean when gaming or fullscreen app is detected</summary>
        public bool PauseWhenFullscreen { get; set; } = true;
        /// <summary>RAM usage % below which auto-clean will never fire</summary>
        public int AutoCleanFloorPercent { get; set; } = 50;

        // --- Theme ---
        public string ThemeName { get; set; } = "Dark";

        // --- AI Optimizer ---
        public AiSettings Ai { get; set; } = new AiSettings();
    }

    // ── Settings manager ──

    public static class SettingsManager
    {
        /// <summary>Raised after settings are saved so long-running timers can reload.</summary>
        public static event Action? Changed;

        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Smart Skii-mer"
        );
        private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

        /// <summary>Restricts the settings file to the current user (best effort).</summary>
        public static void TryHardenAcl(string path)
        {
            try
            {
                var user = Environment.UserName;
                var info = new FileInfo(path);
                var security = info.GetAccessControl();
                security.SetAccessRuleProtection(true, false);
                var rule = new System.Security.AccessControl.FileSystemAccessRule(user, System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.AccessControlType.Allow);
                security.AddAccessRule(rule);
                info.SetAccessControl(security);
            }
            catch { }
        }

        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "Smart Skii-mer";

        public static AppSettings Current { get; private set; } = new AppSettings();

        public static void Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    TryHardenAcl(SettingsPath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        Current = loaded;
                        // Ensure Ai is never null
                        Current.Ai ??= new AiSettings();
                    }
                }
            }
            catch { }

            // Clamp safety bounds
            Current.AutoCleanThresholdPercent = Math.Clamp(Current.AutoCleanThresholdPercent, 40, 95);
            Current.AutoCleanCooldownSeconds = Math.Clamp(Current.AutoCleanCooldownSeconds, 10, 600);
            Current.AutoCleanCheckIntervalSeconds = Math.Clamp(Current.AutoCleanCheckIntervalSeconds, 3, 60);
            Current.AutoCleanFloorPercent = Math.Clamp(Current.AutoCleanFloorPercent, 20, 90);
            Current.TrimMinWorkingSetMB = Math.Clamp(Current.TrimMinWorkingSetMB, 10, 1000);
            Current.NotifyMinFreedMB = Math.Clamp(Current.NotifyMinFreedMB, 0, 500);

            // Apply theme
            ThemeManager.SetTheme(Current.ThemeName ?? "Dark");

            SyncStartupRegistry();
        }

        public static void Save()
        {
            try
            {
                if (!Directory.Exists(SettingsDir))
                {
                    Directory.CreateDirectory(SettingsDir);
                }
                // Never persist the secret in cleartext: the key lives in the DPAPI store.
                var clone = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(Current)) ?? new AppSettings();
                var secret = Current.Ai.ApiKey;
                clone.Ai.ApiKey = "";
                string json = JsonSerializer.Serialize(clone, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
                if (!string.IsNullOrWhiteSpace(secret) && secret.Length > 8) LocalKeyStore.SaveProvider(secret);
                else if (string.IsNullOrWhiteSpace(secret)) LocalKeyStore.DeleteProvider();
                Changed?.Invoke();
            }
            catch { }

            SyncStartupRegistry();
        }

        public static void SetStartWithWindows(bool enable)
        {
            Current.StartWithWindows = enable;
            Save();
        }

        private static void SyncStartupRegistry()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (key != null)
                {
                    string? exePath = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exePath))
                    {
                        exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    }

                    // Clean out old legacy keys
                    try
                    {
                        if (key.GetValue("SmartMemReduct") != null) key.DeleteValue("SmartMemReduct", false);
                        if (key.GetValue("Mem Reduct") != null) key.DeleteValue("Mem Reduct", false);
                    }
                    catch { }

                    if (Current.StartWithWindows && !string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");

                        // Ensure Windows Task Manager marks it as Enabled
                        try
                        {
                            using RegistryKey? approvedKey = Registry.CurrentUser.OpenSubKey(
                                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", true);
                            if (approvedKey != null)
                            {
                                approvedKey.SetValue(AppName, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        if (key.GetValue(AppName) != null)
                        {
                            key.DeleteValue(AppName, false);
                        }
                    }
                }
            }
            catch { }
        }
    }
}
