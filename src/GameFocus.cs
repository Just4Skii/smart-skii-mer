using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SmartMemReduct
{
    /// <summary>
    /// Safe equivalent of a per-game profile: gives the running game a high GPU
    /// preference and AboveNormal CPU priority, protects anti-cheat/overlay processes,
    /// and leaves the rest to the EcoQoS engine. No drivers, clocks or fan curves.
    /// </summary>
    public static class GameFocus
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetPriorityClass(IntPtr process, uint priority);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder name, ref int size);

        private const uint ProcessSetInformation = 0x0200;
        private const uint ProcessQueryLimited = 0x1000;
        private const uint AboveNormal = 0x00008000;
        private const uint Normal = 0x00000020;

        private static readonly string[] ProtectedRelated =
        {
            "easyanticheat", "battleye", "vanguard", "denuvo", "anticheat", "vgc", "vgk",
            "steamwebhelper", "gameoverlayui", "discord", "nvcontainer", "nvsphelper", "amdfendr",
        };

        private static readonly HashSet<string> Applied = new();
        private static int _lastGamePid;

        public static bool IsRelated(string processName)
        {
            var n = processName.ToLowerInvariant();
            return ProtectedRelated.Any(n.Contains);
        }

        public static string Apply(SystemSnapshot snapshot)
        {
            var game = snapshot.Processes.FirstOrDefault(p => p.Foreground);
            if (game is null) return "no foreground game";
            if (!game.GameLikely && !snapshot.ForegroundFullscreenGame) return "no game detected yet";

            try
            {
                var path = ImagePath(game.Pid);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    SetGpuPreference(path, true);
                    Applied.Add(path);
                }
                SetPriority(game.Pid, AboveNormal);
                _lastGamePid = game.Pid;
                return $"{game.Name} prioritised · high GPU preference";
            }
            catch { return $"{game.Name} detected"; }
        }

        public static void Restore()
        {
            if (_lastGamePid != 0)
            {
                try { SetPriority(_lastGamePid, Normal); } catch { }
                _lastGamePid = 0;
            }
        }

        public static void RevertGpuPreferences()
        {
            foreach (var path in Applied.ToList())
                try { SetGpuPreference(path, false); } catch { }
            Applied.Clear();
        }

        private static void SetPriority(int pid, uint priority)
        {
            var h = OpenProcess(ProcessSetInformation | ProcessQueryLimited, false, pid);
            if (h == IntPtr.Zero) return;
            try { SetPriorityClass(h, priority); }
            finally { CloseHandle(h); }
        }

        private static string ImagePath(int pid)
        {
            var h = OpenProcess(ProcessQueryLimited, false, pid);
            if (h == IntPtr.Zero) return "";
            try
            {
                var sb = new System.Text.StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : "";
            }
            finally { CloseHandle(h); }
        }

        private static void SetGpuPreference(string exePath, bool high)
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
            if (key is null) return;
            if (high) key.SetValue(exePath, "GpuPreference=2;", RegistryValueKind.String);
            else key.DeleteValue(exePath, throwOnMissingValue: false);
        }
    }
}
