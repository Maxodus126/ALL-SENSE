using System;
using System.Management;

namespace AllSense.App;

/// <summary>Informasi perangkat via WMI (sekali baca, cache).</summary>
public static class SystemInfo
{
    public static string Manufacturer { get; } = WmiOne("Win32_ComputerSystem", "Manufacturer", "—");
    public static string Model { get; } = WmiOne("Win32_ComputerSystem", "Model", "—");
    public static string CpuName { get; } = WmiOne("Win32_Processor", "Name", "—");
    public static string GpuName { get; } = WmiOne("Win32_VideoController", "Name", "—");
    public static string OsName { get; } = OsString();
    public static string RamTotalLabel { get; } = RamLabel();

    private static string WmiOne(string cls, string prop, string fallback)
    {
        try
        {
            using var mos = new ManagementObjectSearcher($"SELECT {prop} FROM {cls}");
            foreach (ManagementBaseObject o in mos.Get())
            {
                var v = o[prop]?.ToString();
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
        }
        catch { }
        return fallback;
    }

    private static string OsString()
    {
        try { return WmiOne("Win32_OperatingSystem", "Caption", "Windows").Replace("Microsoft ", ""); }
        catch { return "Windows"; }
    }

    private static string RamLabel()
    {
        var (used, total) = NativePow.GetMemoryGB();
        return total > 0 ? $"{total:0.#} GB" : "—";
    }
}
