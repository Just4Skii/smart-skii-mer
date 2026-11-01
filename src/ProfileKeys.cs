using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SmartMemReduct
{
    /// <summary>
    /// Loads API credentials without asking the user to paste them again.
    /// Order: explicit settings → process environment → PowerShell $PROFILE.
    /// </summary>
    public static class ProfileKeys
    {
        public sealed record KeyInfo(string Provider, string Key, string Source);

        public static string ProfilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "WindowsPowerShell",
            "Microsoft.PowerShell_profile.ps1");

        private static Dictionary<string, string> ParseProfile()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(ProfilePath)) return values;
                foreach (var line in File.ReadAllLines(ProfilePath))
                {
                    if (line.TrimStart().StartsWith("#")) continue;
                    var m = Regex.Match(line, @"\$env:([A-Za-z0-9_]+)\s*=\s*[""']([^""']{8,})[""']");
                    if (m.Success) values[m.Groups[1].Value] = m.Groups[2].Value;
                }
            }
            catch { /* a broken profile must not break the app */ }
            return values;
        }

        public static KeyInfo? FindOpenRouter(string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured) && configured.Length > 12)
                return new KeyInfo("OpenRouter", configured, "settings");
            var env = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            if (!string.IsNullOrWhiteSpace(env)) return new KeyInfo("OpenRouter", env!, "environment");
            var p = ParseProfile();
            if (p.TryGetValue("OPENROUTER_API_KEY", out var k)) return new KeyInfo("OpenRouter", k, "PowerShell profile");
            if (p.TryGetValue("ANTHROPIC_AUTH_TOKEN", out var a) && a.StartsWith("sk-or-"))
                return new KeyInfo("OpenRouter", a, "PowerShell profile");
            return null;
        }

        public static KeyInfo? FindNvidia(string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured) && configured.Length > 12)
                return new KeyInfo("NVIDIA NIM", configured, "settings");
            var env = Environment.GetEnvironmentVariable("NVIDIA_API_KEY") ?? Environment.GetEnvironmentVariable("NIM_KEY_QWEN");
            if (!string.IsNullOrWhiteSpace(env)) return new KeyInfo("NVIDIA NIM", env!, "environment");
            var localKey = LocalKeyStore.Load();
            if (!string.IsNullOrWhiteSpace(localKey)) return new KeyInfo("NVIDIA NIM", localKey, "this PC (encrypted)");
            var p = ParseProfile();
            foreach (var name in new[] { "NIM_KEY_SUPER", "NIM_KEY_QWEN", "NIM_KEY_DEEPSEEK", "NIM_KEY_LLAMA", "NIM_KEY_MINIMAX" })
                if (p.TryGetValue(name, out var k)) return new KeyInfo("NVIDIA NIM", k, $"PowerShell profile ({name})");

            return null;
        }

        /// <summary>Never includes the raw key; safe for logs and the UI.</summary>
        public static string Mask(string key) =>
            key.Length <= 10 ? "••••" : key[..6] + "…" + key[^4..];
    }
}
