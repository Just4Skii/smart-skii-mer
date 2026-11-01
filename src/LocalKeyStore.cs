using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SmartMemReduct
{
    /// <summary>
    /// Stores the optional NVIDIA NIM key encrypted with Windows DPAPI for the
    /// current user only. It never goes into source control or the application binary.
    /// </summary>
    internal static class LocalKeyStore
    {
        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Smart Skii-mer",
            "nvidia.key");

        private static string CustomPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Smart Skii-mer",
            "provider.key");

        /// <summary>Saves the user-chosen provider key (any vendor) encrypted for this Windows user.</summary>
        public static void SaveProvider(string key) => Write(CustomPath, key);

        public static string? LoadProvider() => Read(CustomPath);

        public static void DeleteProvider()
        {
            try { if (File.Exists(CustomPath)) File.Delete(CustomPath); } catch { }
        }

        private static void Write(string path, string key)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(path, encrypted);
                SettingsManager.TryHardenAcl(path);
            }
            catch { }
        }

        private static string? Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var raw = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
                var key = Encoding.UTF8.GetString(raw).Trim();
                return key.Length > 8 ? key : null;
            }
            catch { return null; }
        }

        public static void Save(string key)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(dir);
                var encrypted = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(key.Trim()),
                    null,
                    DataProtectionScope.CurrentUser);
                File.WriteAllBytes(FilePath, encrypted);
                SettingsManager.TryHardenAcl(FilePath);
            }
            catch { /* key storage must never crash the app */ }
        }

        public static void Delete()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        }

        public static string? Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var raw = ProtectedData.Unprotect(
                    File.ReadAllBytes(FilePath),
                    null,
                    DataProtectionScope.CurrentUser);
                var key = Encoding.UTF8.GetString(raw).Trim();
                return key.StartsWith("nvapi-") ? key : null;
            }
            catch { return null; }
        }
    }
}
