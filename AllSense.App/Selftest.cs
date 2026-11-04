using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace AllSense.App;

/// <summary>Headless self-test: `--selftest [log]`. Marker per-area di %TEMP%\AllSense-selftest. Exit 0 = semua lolos.</summary>
public static class Selftest
{
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "AllSense-selftest");

    public static int Run(string? logPath = null)
    {
        try { Directory.CreateDirectory(Dir); } catch { }

        int fails = 0;

        void Mark(string area, bool ok, bool count = true)
        {
            try
            {
                foreach (var ext in new[] { ".ok", ".fail" })
                {
                    var f = Path.Combine(Dir, area + ext);
                    if (File.Exists(f)) File.Delete(f);
                }
                File.WriteAllText(Path.Combine(Dir, area + (ok ? ".ok" : ".fail")), "1");
            }
            catch { }
            if (!ok && count) fails++;
        }

        // 1. Power mode
        try
        {
            bool okA = NativePow.TryGetAc(out var ac);
            bool okD = NativePow.TryGetDc(out var dc);
            Mark("power_read", okA && okD);

            if (okA && okD)
            {
                bool modA = NativePow.SetAc(PowerMode.Balanced, out var eA);
                bool modD = NativePow.SetDc(PowerMode.Balanced, out var eD);
                bool mod = modA && modD;
                Mark("power_modern", mod, count: false);               // diagnostik: fallback skema menutup
                Mark($"pow_modern_err_{eA}_{eD}", !mod, count: false);

                bool ok = mod;
                if (!mod)
                {
                    bool sc = NativePow.SetActiveScheme(PowerMode.Balanced);
                    Mark("power_scheme_fb", sc);
                    ok = sc;
                }
                Mark("power", ok);

                // Pulihkan nilai asli (best-effort)
                NativePow.SetAc(ac, out _);
                NativePow.SetDc(dc, out _);

                // Round-trip Best Performance: set lalu baca ulang (bukti fitur bekerja).
                try
                {
                    bool pSet = NativePow.SetAc(PowerMode.Performance, out var eP)
                                && NativePow.TryGetAc(out var gP) && gP == PowerMode.Performance;
                    Mark($"perf_roundtrip_err_{eP}", !pSet, count: false);
                    Mark("perf_roundtrip", pSet, count: false);
                    NativePow.SetAc(ac, out _);
                    NativePow.SetDc(dc, out _);
                }
                catch { Mark("perf_roundtrip", false, count: false); }
            }
            else Mark("power", false);
        }
        catch { Mark("power", false); }

        // 2. Sensor
        var diag = new StringBuilder();
        try
        {
            using var hw = new HardwareMonitor();
            bool got = false;
            for (int i = 0; i < 12 && !got; i++) { Thread.Sleep(500); got = hw.Available; }
            if (got)
            {
                try { hw.Poll(); } catch { }

                // Diagnostik: daftar semua sensor suhu + nilainya.
                try
                {
                    foreach (var hwd in hw.Hardware)
                    {
                        hwd.Update();
                        foreach (var s in hwd.Sensors)
                        {
                            if (s.SensorType != SensorType.Temperature) continue;
                            string v = s.Value.HasValue ? s.Value.Value.ToString("F1") : "-";
                            diag.AppendLine($"  {hwd.HardwareType} / {hwd.Name} / {s.Name} = {v}");
                        }
                    }
                }
                catch { }

                Mark("sensors", true);
                Mark("cpu_sensor", !float.IsNaN(hw.CpuTemperatureC));
                Mark("gpu_sensor", !float.IsNaN(hw.GpuTemperatureC));
            }
            else Mark("sensors", false);
        }
        catch { Mark("sensors", false); }

        // 3. WMI
        try
        {
            bool wmi = !string.IsNullOrWhiteSpace(SystemInfo.Manufacturer) && SystemInfo.Manufacturer != "—"
                       && !string.IsNullOrWhiteSpace(SystemInfo.CpuName);
            Mark("wmi", wmi);
        }
        catch { Mark("wmi", false); }

        // 4. Storage
        try
        {
            var d = DriveInfo.GetDrives().FirstOrDefault(x => x.IsReady && x.DriveType == DriveType.Fixed && x.TotalSize > 0);
            Mark("storage", d != null);
        }
        catch { Mark("storage", false); }

        // 5. Memori
        try
        {
            var (_, total) = NativePow.GetMemoryGB();
            Mark("memory", total > 0);
        }
        catch { Mark("memory", false); }

        // 6. Versi & shortcut
        try { _ = Version.Parse(AppInfo.VersionString); Mark("version", true); }
        catch { Mark("version", false); }
        try { Mark("update_tagparse", AppUpdate.TryParseTag("v1.2.0", out var _)); }
        catch { Mark("update_tagparse", false); }
        try { Mark("hotkey_format", Hotkey.Format(Hotkey.MOD_CONTROL | Hotkey.MOD_ALT, 0x41) == "Ctrl+Alt+A"); }
        catch { Mark("hotkey_format", false); }

        Mark("overall", fails == 0);

        var sb = new StringBuilder();
        sb.AppendLine("ALLSENSE SELFTEST " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("RESULT: " + (fails == 0 ? "PASS" : fails + " FAILURE(S)"));
        foreach (var f in Directory.GetFiles(Dir, "*"))
            sb.AppendLine(Path.GetFileName(f));

        if (diag.Length > 0)
        {
            sb.AppendLine("SENSORS:");
            sb.Append(diag);
        }

        try
        {
            string target = logPath ?? Path.Combine(Dir, "report.txt");
            File.WriteAllText(target, sb.ToString());
        }
        catch { }

        return fails == 0 ? 0 : 1;
    }
}
