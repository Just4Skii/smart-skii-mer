using System;
using System.Windows.Forms;

namespace SmartMemReduct
{
    public class HotkeyManager : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;

        public const int ID_HOTKEY_SMART_CLEAN = 1001;
        public const int ID_HOTKEY_EMERGENCY_UNFREEZE = 1002;
        public const int ID_HOTKEY_CPU_RELIEF = 1003;
        public const int ID_HOTKEY_DEEP_CLEAN = 1004;

        public event Action? OnSmartCleanTriggered;
        public event Action? OnEmergencyUnfreezeTriggered;
        public event Action? OnCpuReliefTriggered;
        public event Action? OnDeepCleanTriggered;

        public HotkeyManager()
        {
            CreateHandle(new CreateParams());
            RegisterHotkeys();
        }

        public void RegisterHotkeys()
        {
            UnregisterHotkeys();
            // Ctrl + Alt + M  →  Smart RAM Clean (auto-selects level)
            NativeMethods.RegisterHotKey(
                Handle, ID_HOTKEY_SMART_CLEAN,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
                (uint)Keys.M);

            // Ctrl + Alt + F  →  Emergency Unfreeze
            NativeMethods.RegisterHotKey(
                Handle, ID_HOTKEY_EMERGENCY_UNFREEZE,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
                (uint)Keys.F);

            // Ctrl + Alt + K  →  Throttle Background CPU Hogs
            NativeMethods.RegisterHotKey(
                Handle, ID_HOTKEY_CPU_RELIEF,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
                (uint)Keys.K);

            // Ctrl + Alt + D  →  Deep Clean (maximum safe clean)
            NativeMethods.RegisterHotKey(
                Handle, ID_HOTKEY_DEEP_CLEAN,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT,
                (uint)Keys.D);
        }

        private static (uint mods, Keys key) ParseHotkey(string text)
        {
            uint mods = 0;
            var key = Keys.D;
            foreach (var part in (text ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= NativeMethods.MOD_CONTROL; break;
                    case "alt": mods |= NativeMethods.MOD_ALT; break;
                    case "shift": mods |= NativeMethods.MOD_SHIFT; break;
                    case "win": mods |= NativeMethods.MOD_WIN; break;
                    default:
                        if (Enum.TryParse<Keys>(part, true, out var k)) key = k;
                        break;
                }
            }
            if (mods == 0) mods = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT;
            return (mods, key);
        }

        public void UnregisterHotkeys()
        {
            NativeMethods.UnregisterHotKey(Handle, ID_HOTKEY_SMART_CLEAN);
            NativeMethods.UnregisterHotKey(Handle, ID_HOTKEY_EMERGENCY_UNFREEZE);
            NativeMethods.UnregisterHotKey(Handle, ID_HOTKEY_CPU_RELIEF);
            NativeMethods.UnregisterHotKey(Handle, ID_HOTKEY_DEEP_CLEAN);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                switch (id)
                {
                    case ID_HOTKEY_SMART_CLEAN:
                        OnSmartCleanTriggered?.Invoke();
                        break;
                    case ID_HOTKEY_EMERGENCY_UNFREEZE:
                        OnEmergencyUnfreezeTriggered?.Invoke();
                        break;
                    case ID_HOTKEY_CPU_RELIEF:
                        OnCpuReliefTriggered?.Invoke();
                        break;
                    case ID_HOTKEY_DEEP_CLEAN:
                        OnDeepCleanTriggered?.Invoke();
                        break;
                }
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            UnregisterHotkeys();
            DestroyHandle();
        }
    }
}
