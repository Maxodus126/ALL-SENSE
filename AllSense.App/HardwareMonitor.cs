using System;
using System.Management;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace AllSense.App;

/// <summary>Bungkus LibreHardwareMonitor untuk ambil suhu CPU/GPU/RAM.</summary>
public sealed class HardwareMonitor : IDisposable
{
    private readonly Computer _computer;
    private readonly object _lock = new();
    private float _cpuC = float.NaN;
    private float _gpuC = float.NaN;
    private float _ramC = float.NaN;
    private float _cpuLoad = float.NaN;
    private float _gpuLoad = float.NaN;
    private float _fanRpm = float.NaN;
    private bool _cpuZone;
    private bool _opened;

    public HardwareMonitor()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,   // sering jadi sumber suhu CPU di laptop (EC)
            IsControllerEnabled = true,
            IsStorageEnabled = false,
            IsNetworkEnabled = false,
        };

        // Buka sensor di thread belakang — jangan blokir UI; aman walau gagal.
        try
        {
            var t = new Thread(() =>
            {
                try { _computer.Open(); lock (_lock) _opened = true; }
                catch { lock (_lock) _opened = false; }
            }) { IsBackground = true };
            t.Start();
        }
        catch { }
    }

    public bool Available
    {
        get { lock (_lock) return _opened; }
    }

    public System.Collections.Generic.IEnumerable<IHardware> Hardware
    {
        get { lock (_lock) return _opened ? _computer.Hardware : Array.Empty<IHardware>(); }
    }

    /// <summary>Suhu mainboard/sistem (°C) dari zona termal ACPI; NaN bila zona tak terbaca.</summary>
    public float CpuTemperatureC { get { lock (_lock) return _cpuC; } }
    public float GpuTemperatureC { get { lock (_lock) return _gpuC; } }
    public float MemoryTemperatureC { get { lock (_lock) return _ramC; } }
    public float CpuLoadPercent { get { lock (_lock) return _cpuLoad; } }
    public float GpuLoadPercent { get { lock (_lock) return _gpuLoad; } }

    /// <summary>Kecepatan kipas (RPM). NaN bila tidak ada sensor Fan.</summary>
    public float FanRpm { get { lock (_lock) return _fanRpm; } }

    /// <summary>Suhu CPU selalu bernilai true — kami menampilkan zona termal ACPI (mainboard/sistem).</summary>
    public bool CpuViaThermalZone { get { lock (_lock) return _cpuZone; } }

    /// <summary>true bila zona termal ACPI tak terbaca (suhu mainboard = NaN).</summary>
    public bool CpuTempMissing { get { lock (_lock) return float.IsNaN(_cpuC); } }

    /// <summary>Panggil tiap ~2 dtk. Jangan panggil dari thread UI (bisa lambat).</summary>
    public void Poll()
    {
        bool opened;
        lock (_lock) opened = _opened;
        if (!opened) return;

        float cpu = float.NaN, gpu = float.NaN, ram = float.NaN;
        float cpuLoad = float.NaN, gpuLoad = float.NaN;
        float fan = float.NaN;

        try
        {
            foreach (var hw in _computer.Hardware)
            {
                hw.Update();

                // Kipas: skan semua sensor terlepas dari jenis hardware.
                foreach (var s in hw.Sensors)
                {
                    if (s.SensorType != SensorType.Fan || !s.Value.HasValue) continue;
                    float v = s.Value.Value;
                    if (!float.IsFinite(v)) continue;
                    fan = float.IsNaN(fan) ? v : Math.Max(fan, v);
                }

                switch (hw.HardwareType)
                {
                    case HardwareType.Cpu:
                        // Suhu inti CPU (LHM) diabaikan — butuh driver ring0 yang tak selalu
                        // bisa dimuat. Suhu "CPU" yang kami tampilkan berasal dari zona termal
                        // ACPI (suhu mainboard/sistem), lihat fallback di bawah.
                        foreach (var s in hw.Sensors)
                        {
                            if (s.SensorType != SensorType.Load || !s.Value.HasValue) continue;
                            float v = s.Value.Value;
                            if (!float.IsFinite(v)) continue;
                            cpuLoad = float.IsNaN(cpuLoad) ? v : Math.Max(cpuLoad, v);
                        }
                        break;
                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        {
                            float tBest = float.NaN, tCore = float.NaN;
                            foreach (var s in hw.Sensors)
                            {
                                if (!s.Value.HasValue) continue;
                                float v = s.Value.Value;
                                if (!float.IsFinite(v)) continue;
                                if (s.SensorType == SensorType.Temperature)
                                {
                                    // Prioritaskan sensor inti (GPU Core): Hot Spot / Memory Junction
                                    // jauh lebih panas dan bukan suhu yang dibandingkan orang dgn Task Manager.
                                    if (s.Name.Contains("core", StringComparison.OrdinalIgnoreCase))
                                        tCore = float.IsNaN(tCore) ? v : Math.Max(tCore, v);
                                    else
                                        tBest = float.IsNaN(tBest) ? v : Math.Max(tBest, v);
                                }
                                else if (s.SensorType == SensorType.Load)
                                    gpuLoad = float.IsNaN(gpuLoad) ? v : Math.Max(gpuLoad, v);
                            }
                            float tg = float.IsNaN(tCore) ? tBest : tCore;
                            if (!float.IsNaN(tg))
                                gpu = float.IsNaN(gpu) ? tg : Math.Max(gpu, tg);
                        }
                        break;
                    case HardwareType.Memory:
                        foreach (var s in hw.Sensors)
                        {
                            if (s.SensorType != SensorType.Temperature || !s.Value.HasValue) continue;
                            float v = s.Value.Value;
                            if (!float.IsFinite(v)) continue;
                            ram = float.IsNaN(ram) ? v : Math.Max(ram, v);
                        }
                        break;
                }
            }
        }
        catch { return; }

        // Suhu "CPU" = suhu mainboard/sistem dari zona termal ACPI (WMI). Selalu
        // dipakai, bukan hanya fallback — jujur & konsisten di semua laptop.
        bool zone = false;
        cpu = GetCpuViaWmi();
        zone = !float.IsNaN(cpu);

        lock (_lock) { _cpuC = cpu; _gpuC = gpu; _ramC = ram; _cpuLoad = cpuLoad; _gpuLoad = gpuLoad; _fanRpm = fan; _cpuZone = zone; }
    }

    private static float GetCpuViaWmi()
    {
        float best = float.NaN;
        void Track(double c)
        {
            if (c > -50 && c < 150) best = float.IsNaN(best) ? (float)c : Math.Max(best, (float)c);
        }

        // 1) Zona termal yang sudah diagregasi (persepuluh Kelvin / Kelvin).
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
            foreach (ManagementBaseObject o in searcher.Get())
            {
                if (o["Temperature"] is not { } v) continue;
                double d = Convert.ToDouble(v);
                Track(d / 10.0 - 273.15);
                Track(d - 273.15);
            }
        }
        catch { }

        // 2) Zona termal ACPI mentah (MSAcpi) — CurrentTemperature = persepuluh Kelvin.
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM MSAcpi_ThermalZoneTemperature");
            foreach (ManagementBaseObject o in searcher.Get())
            {
                if (o["CurrentTemperature"] is not { } v) continue;
                Track(Convert.ToDouble(v) / 10.0 - 273.15);
            }
        }
        catch { }

        return best;
    }

    public void Dispose()
    {
        try { if (Available) _computer.Close(); } catch { }
    }
}
