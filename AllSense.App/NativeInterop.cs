using System;
using System.Management;
using System.Runtime.InteropServices;

namespace AllSense.App;

public enum PowerMode
{
    Efficiency = 0,
    Balanced = 1,
    Performance = 2,
}

public static class PowerModeGuids
{
    // GUID skema daya (untuk PowerSetActiveScheme).
    public static readonly Guid SchemeBestEfficiency = new("a4f06079-f3e9-45e0-8562-8aa743b4e001");
    public static readonly Guid SchemeBalanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid SchemeBestPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    // GUID power-mode slider Windows 11 (untuk PowerSetUserConfigured{AC,DC}PowerMode).
    public static readonly Guid ModeBestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid ModeBalanced = Guid.Empty;   // GUID_POWER_MODE_NONE
    public static readonly Guid ModeBestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    public static Guid ToSchemeGuid(PowerMode m) => m switch
    {
        PowerMode.Efficiency => SchemeBestEfficiency,
        PowerMode.Performance => SchemeBestPerformance,
        _ => SchemeBalanced,
    };

    public static Guid ToModeGuid(PowerMode m) => m switch
    {
        PowerMode.Efficiency => ModeBestEfficiency,
        PowerMode.Performance => ModeBestPerformance,
        _ => ModeBalanced,
    };

    public static PowerMode FromModeGuid(Guid g)
    {
        if (g == ModeBestEfficiency) return PowerMode.Efficiency;
        if (g == ModeBestPerformance) return PowerMode.Performance;
        return PowerMode.Balanced;
    }
}

/// <summary>P/Invoke ke powrprof.dll untuk power mode + kernel32 untuk status sistem.</summary>
public static class NativePow
{
    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    private static extern uint PowerGetUserConfiguredACPowerMode(out Guid mode);

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    private static extern uint PowerGetUserConfiguredDCPowerMode(out Guid mode);

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    private static extern uint PowerSetUserConfiguredACPowerMode(Guid mode);

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    private static extern uint PowerSetUserConfiguredDCPowerMode(Guid mode);

    /// <summary>Ganti skema daya aktif (fallback klasik, tanpa overlay slider modern).</summary>
    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

    public static bool SetActiveScheme(PowerMode m)
    {
        var g = PowerModeGuids.ToSchemeGuid(m);
        return PowerSetActiveScheme(IntPtr.Zero, ref g) == 0;
    }

    public static bool TryGetAc(out PowerMode mode)
    {
        if (PowerGetUserConfiguredACPowerMode(out var g) == 0) { mode = PowerModeGuids.FromModeGuid(g); return true; }
        mode = PowerMode.Balanced; return false;
    }

    public static bool TryGetDc(out PowerMode mode)
    {
        if (PowerGetUserConfiguredDCPowerMode(out var g) == 0) { mode = PowerModeGuids.FromModeGuid(g); return true; }
        mode = PowerMode.Balanced; return false;
    }

    public static bool SetAc(PowerMode m) => PowerSetUserConfiguredACPowerMode(PowerModeGuids.ToModeGuid(m)) == 0;
    public static bool SetDc(PowerMode m) => PowerSetUserConfiguredDCPowerMode(PowerModeGuids.ToModeGuid(m)) == 0;

    public static bool SetAc(PowerMode m, out uint err)
    {
        err = PowerSetUserConfiguredACPowerMode(PowerModeGuids.ToModeGuid(m));
        return err == 0;
    }

    public static bool SetDc(PowerMode m, out uint err)
    {
        err = PowerSetUserConfiguredDCPowerMode(PowerModeGuids.ToModeGuid(m));
        return err == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;      // 0 = batere, 1 = AC, 255 = tidak diketahui
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    /// <summary>true = pakai AC, false = pakai baterai.</summary>
    public static bool? IsOnAC()
    {
        if (!GetSystemPowerStatus(out var s)) return null;
        if (s.ACLineStatus == 1) return true;
        if (s.ACLineStatus == 0) return false;
        return null;
    }

    /// <summary>
    /// Info baterai via GetSystemPowerStatus + WMI Win32_Battery (bila tersedia).
    /// ChargePercent = EstimatedChargeRemaining; BatteryStatus = kode WMI; LifeSeconds/FullLifeSeconds dari struct.
    /// </summary>
    public static (int? ChargePercent, int? BatteryStatus, int? LifeSeconds, int? FullLifeSeconds) GetBatteryInfo()
    {
        if (!GetSystemPowerStatus(out var s)) return (null, null, null, null);

        int? pct = s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : (int?)null;
        int? life = s.BatteryLifeTime == uint.MaxValue ? null : (int?)s.BatteryLifeTime;
        int? full = s.BatteryFullLifeTime == uint.MaxValue ? null : (int?)s.BatteryFullLifeTime;

        int? status = null;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT BatteryStatus FROM Win32_Battery");
            foreach (ManagementBaseObject o in searcher.Get())
            {
                if (o["BatteryStatus"] != null)
                {
                    status = Convert.ToInt32(o["BatteryStatus"]);
                    break;
                }
            }
        }
        catch { }

        return (pct, status, life, full);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
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
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buf);

    /// <summary>(terpakai GB, total GB).</summary>
    public static (double UsedGB, double TotalGB) GetMemoryGB()
    {
        var b = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref b) || b.ullTotalPhys == 0) return (0, 0);
        double total = b.ullTotalPhys / 1024d / 1024d / 1024d;
        double avail = b.ullAvailPhys / 1024d / 1024d / 1024d;
        return (total - avail, total);
    }
}

/// <summary>Shortcut global (RegisterHotKey) untuk memanggil AllSense ke depan.</summary>
public static class Hotkey
{
    public const int WM_HOTKEY = 0x0312;
    public const int HOTKEY_ID = 0xE100;
    public const uint MOD_ALT = 0x1;
    public const uint MOD_CONTROL = 0x2;
    public const uint MOD_SHIFT = 0x4;
    public const uint MOD_WIN = 0x8;

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>Rangkai nama kombinasi, mis. "Ctrl+Alt+A".</summary>
    public static string Format(uint mods, uint vk)
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((mods & MOD_WIN) != 0) parts.Add("Win");
        if ((mods & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & MOD_ALT) != 0) parts.Add("Alt");
        if ((mods & MOD_SHIFT) != 0) parts.Add("Shift");
        parts.Add(KeyName(vk));
        return string.Join("+", parts);
    }

    private static string KeyName(uint vk)
    {
        if (vk >= 'A' && vk <= 'Z') return ((char)vk).ToString();
        if (vk >= '0' && vk <= '9') return ((char)vk).ToString();
        return vk switch
        {
            0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x1B => "Esc",
            0x20 => "Space", 0x21 => "PageUp", 0x22 => "PageDown",
            0x23 => "End", 0x24 => "Home",
            0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
            >= 0x70 and <= 0x7B => "F" + (vk - 0x6F),
            _ => "[" + vk + "]",
        };
    }
}
