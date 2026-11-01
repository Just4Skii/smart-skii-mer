using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SmartMemReduct
{
    public static class NativeMethods
    {
        // --- ntdll.dll ---
        [DllImport("ntdll.dll")]
        public static extern int NtSetSystemInformation(int SystemInformationClass, ref int SystemInformation, int SystemInformationLength);

        // --- advapi32.dll ---
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out long lpLuid);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public long Luid;
            public int Attributes;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, int BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        // --- psapi.dll ---
        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool EmptyWorkingSet(IntPtr hProcess);

        // --- kernel32.dll ---
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        // --- user32.dll ---
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        // --- Structs ---
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        // --- Performance counters for standby/modified memory ---
        [StructLayout(LayoutKind.Sequential)]
        public struct PERFORMANCE_INFORMATION
        {
            public uint cb;
            public UIntPtr CommitTotal;
            public UIntPtr CommitLimit;
            public UIntPtr CommitPeak;
            public UIntPtr PhysicalTotal;
            public UIntPtr PhysicalAvailable;
            public UIntPtr SystemCache;
            public UIntPtr KernelTotal;
            public UIntPtr KernelPaged;
            public UIntPtr KernelNonpaged;
            public UIntPtr PageSize;
            public uint HandleCount;
            public uint ProcessCount;
            public uint ThreadCount;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pPerformanceInformation, uint cb);

        // --- Constants ---
        public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        public const uint TOKEN_QUERY = 0x0008;
        public const int SE_PRIVILEGE_ENABLED = 0x00000002;

        public const int MOD_ALT = 0x0001;
        public const int MOD_CONTROL = 0x0002;
        public const int MOD_SHIFT = 0x0004;
        public const int MOD_WIN = 0x0008;
        public const int MOD_NOREPEAT = 0x4000;

        // SystemMemoryListInformation commands
        public const int SystemMemoryListInformation = 80;
        public const int MemoryFlushModifiedList = 3;
        public const int MemoryPurgeStandbyList = 4;
        public const int MemoryPurgeLowPriorityStandbyList = 5;

        // GW_OWNER for GetWindow
        public const uint GW_OWNER = 4;

        // --- Privilege helpers ---
        public static void EnablePrivilege(string privilegeName)
        {
            try
            {
                IntPtr hProcess = GetCurrentProcess();
                if (OpenProcessToken(hProcess, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hToken))
                {
                    try
                    {
                        if (LookupPrivilegeValue(null, privilegeName, out long luid))
                        {
                            TOKEN_PRIVILEGES tp = new TOKEN_PRIVILEGES
                            {
                                PrivilegeCount = 1,
                                Luid = luid,
                                Attributes = SE_PRIVILEGE_ENABLED
                            };
                            AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                        }
                    }
                    finally
                    {
                        CloseHandle(hToken);
                    }
                }
            }
            catch { }
        }

        // --- dwmapi.dll (Window Theme, Border, and Title Bar) ---
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_BORDER_COLOR = 34;
        public const int DWMWA_CAPTION_COLOR = 35;
        public const int DWMWA_TEXT_COLOR = 36;

        public static void ApplyDarkModeToWindow(IntPtr hWnd, bool darkMode, System.Drawing.Color? captionColor = null, System.Drawing.Color? borderColor = null, System.Drawing.Color? textColor = null)
        {
            try
            {
                int useDark = darkMode ? 1 : 0;
                int hr = DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
                if (hr != 0)
                {
                    DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDark, sizeof(int));
                }

                if (captionColor.HasValue)
                {
                    int colorRef = (captionColor.Value.R) | (captionColor.Value.G << 8) | (captionColor.Value.B << 16);
                    DwmSetWindowAttribute(hWnd, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
                }

                if (borderColor.HasValue)
                {
                    int colorRef = (borderColor.Value.R) | (borderColor.Value.G << 8) | (borderColor.Value.B << 16);
                    DwmSetWindowAttribute(hWnd, DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
                }

                if (textColor.HasValue)
                {
                    int colorRef = (textColor.Value.R) | (textColor.Value.G << 8) | (textColor.Value.B << 16);
                    DwmSetWindowAttribute(hWnd, DWMWA_TEXT_COLOR, ref colorRef, sizeof(int));
                }
            }
            catch { }
        }

        public static void EnableAllRequiredPrivileges()
        {
            EnablePrivilege("SeIncreaseQuotaPrivilege");
            EnablePrivilege("SeProfileSingleProcessPrivilege");
            EnablePrivilege("SeDebugPrivilege");
        }
    }
}
