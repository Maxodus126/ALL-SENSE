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
    private float _cpuC = float.NaN;      // preferensi: inti CPU (LHM); fallback zona ACPI
    private float _gpuC = float.NaN;
    private float _ramC = float.NaN;
    private float _cpuLoad = float.NaN;
    private float _gpuLoad = float.NaN;
    private float _fanRpm = float.NaN;
    private bool _cpuViaCore;             // true = suhu inti CPU asli terbaca
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

    /// <summary>Suhu CPU (°C): inti asli (LHM) bila terbaca, selain itu zona termal ACPI.</summary>
    public float CpuTemperatureC { get { lock (_lock) return _cpuC; } }

    /// <summary>true bila suhu inti CPU asli terbaca (bukan zona ACPI).</summary>
    public bool CpuViaCore { get { lock (_lock) return _cpuViaCore; } }
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
        float cpuCore = float.NaN;
        float cpuLoad = float.NaN, gpuLoad = float.NaN;
        float fan = float.NaN;

        // Akumulator GPU per kelas: diskret vs iGPU — hindari mencampur & memakai max antar-GPU.
        float dCore = float.NaN, dSafe = float.NaN, dOther = float.NaN, dLoad = float.NaN;
        float iCore = float.NaN, iSafe = float.NaN, iOther = float.NaN, iLoad = float.NaN;
        bool hasDiscrete = false, hasIgpu = false;

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
                        // Suhu inti CPU asli (LHM): dipakai kalau terbaca (butuh akses MSR);
                        // kalau tidak, jatuh ke zona termal ACPI di bawah.
                        float cpuCoreT = float.NaN;
                        foreach (var s in hw.Sensors)
                        {
                            if (!s.Value.HasValue) continue;
                            float v = s.Value.Value;
                            if (!float.IsFinite(v)) continue;
                            if (s.SensorType == SensorType.Temperature)
                                cpuCoreT = float.IsNaN(cpuCoreT) ? v : Math.Max(cpuCoreT, v);
                            else if (s.SensorType == SensorType.Load)
                                cpuLoad = float.IsNaN(cpuLoad) ? v : Math.Max(cpuLoad, v);
                        }
                        cpuCore = float.IsNaN(cpuCore) ? cpuCoreT : Math.Max(cpuCore, cpuCoreT);
                        break;
                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        {
                            bool disc = hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd;
                            float core = float.NaN, safe = float.NaN, other = float.NaN, load = float.NaN;
                            foreach (var s in hw.Sensors)
                            {
                                if (!s.Value.HasValue) continue;
                                float v = s.Value.Value;
                                if (!float.IsFinite(v)) continue;
                                if (s.SensorType == SensorType.Temperature)
                                {
                                    string n = s.Name.ToLowerInvariant();
                                    // Prioritas: inti (GPU Core) > suhu umum aman (GPU Temperature)
                                    // > lainnya (Hot Spot / Junction / Memory — dipakai hanya kalau tak ada lagi).
                                    if (n.Contains("core"))
                                        core = float.IsNaN(core) ? v : Math.Max(core, v);
                                    else if (n.Contains("hot") || n.Contains("junction") || n.Contains("memory") || n.Contains("vram"))
                                        other = float.IsNaN(other) ? v : Math.Max(other, v);
                                    else
                                        safe = float.IsNaN(safe) ? v : Math.Max(safe, v);
                                }
                                else if (s.SensorType == SensorType.Load)
                                    load = float.IsNaN(load) ? v : Math.Max(load, v);
                            }
                            if (disc) { hasDiscrete = true; dCore = Max(dCore, core); dSafe = Max(dSafe, safe); dOther = Max(dOther, other); dLoad = Max(dLoad, load); }
                            else { hasIgpu = true; iCore = Max(iCore, core); iSafe = Max(iSafe, safe); iOther = Max(iOther, other); iLoad = Max(iLoad, load); }
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

        // Suhu "CPU": preferensi inti asli (LHM); kalau tak terbaca, fallback zona ACPI.
        bool zone = false;
        float zoneC = GetCpuViaWmi();
        zone = !float.IsNaN(zoneC);
        if (!float.IsNaN(cpuCore)) { cpu = cpuCore; }
        else if (zone) { cpu = zoneC; }

        // Suhu "GPU": pakai GPU diskret kalau ada, selain itu iGPU; inti > aman > lainnya.
        if (hasDiscrete) { gpu = PickGpuTemp(dCore, dSafe, dOther); gpuLoad = dLoad; }
        else if (hasIgpu) { gpu = PickGpuTemp(iCore, iSafe, iOther); gpuLoad = iLoad; }

        lock (_lock)
        {
            _cpuC = cpu; _gpuC = gpu; _ramC = ram; _cpuLoad = cpuLoad; _gpuLoad = gpuLoad;
            _fanRpm = fan; _cpuZone = zone; _cpuViaCore = !float.IsNaN(cpuCore);
        }
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

    private static float Max(float a, float b)
        => float.IsNaN(a) ? b : float.IsNaN(b) ? a : Math.Max(a, b);

    private static float PickGpuTemp(float core, float safe, float other)
        => !float.IsNaN(core) ? core : !float.IsNaN(safe) ? safe : other;

    public void Dispose()
    {
        try { if (Available) _computer.Close(); } catch { }
    }
}
