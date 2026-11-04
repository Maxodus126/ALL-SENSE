using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AllSense.App;

public partial class MainWindow : Window
{
    private readonly HardwareMonitor _hw = new();
    private readonly DispatcherTimer _timer = new();
    private PowerMode _acMode = PowerMode.Balanced;
    private PowerMode _dcMode = PowerMode.Balanced;
    private string _method = "modern";
    private bool _linked = true;
    private bool _closed;
    private bool _polling;

    // Shortcut global.
    private HwndSource? _hwndSource;
    private uint _hMods = Hotkey.MOD_CONTROL | Hotkey.MOD_ALT;   // Ctrl+Alt
    private uint _hKey = 0x41;                                   // A
    private bool _hkWarned;

    // Update.
    private AppUpdate.Result? _lastUpdate;
    private string _updateRepo = AppUpdate.DefaultRepo;

    // Pengaturan.
    private readonly string _settingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AllSense");
    private string _settingsFile => Path.Combine(_settingsDir, "settings.json");
    private bool _startWithWindows;
    private int _sampleIntervalSec = 2;
    private bool _minimizeToTray;
    private bool _trayExitRequested;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    private sealed class Settings
    {
        public bool StartWithWindows { get; set; }
        public bool MinimizeToTray { get; set; }
        public int SampleIntervalSec { get; set; } = 2;
        public int HotkeyModifiers { get; set; } = (int)(Hotkey.MOD_CONTROL | Hotkey.MOD_ALT);
        public int HotkeyKey { get; set; } = 0x41;
        public string UpdateRepo { get; set; } = "";
    }

    private static readonly (string Key, string Title, string Crumb)[] Pages =
    {
        ("dashboard", "DASHBOARD", "Ringkasan sistem"),
        ("power", "POWER CENTER", "Gaya performa"),
        ("cooling", "COOLING", "Termal & kipas"),
        ("battery", "BATTERY", "Daya & kesehatan"),
        ("mylaptop", "MY LAPTOP", "Spesifikasi"),
        ("storage", "STORAGE", "Volume & drive"),
        ("gaming", "GAMING HUB", "Boost & perpustakaan"),
        ("settings", "SETTINGS", "Preferensi"),
        ("about", "ABOUT", "Tentang"),
    };

    public MainWindow()
    {
        InitializeComponent();

        LoadSettings();
        if (_startWithWindows) ApplyStartWithWindows(true);
        if (_minimizeToTray) SetupTray();

        _timer.Tick += (_, _) => Tick();
        _timer.Interval = TimeSpan.FromSeconds(_sampleIntervalSec);
        _timer.Start();

        SourceInitialized += (_, _) =>
        {
            TrySetDarkTitleBar();
            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            _hwndSource?.AddHook(WndProc);
            RegisterHotkey();
        };

        Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            var h = _hwndSource?.Handle ?? IntPtr.Zero;
            if (h != IntPtr.Zero) Hotkey.UnregisterHotKey(h, Hotkey.HOTKEY_ID);
            _hw.Dispose();
            SaveSettings();
            DisposeTray();
        };

        PopulateSystem();
        PopulateStorage();
        ReadPowerState();
        SelectPage("dashboard");

        Loaded += (_, _) =>
        {
            AutoCheckUpdate();
            EnsureVisibleAtStartup();
        };
    }

    /// <summary>Jaga s jendela beneran tampil sesudah load: pulihkan kalau minim & ke slope windows
    /// (kadang terjadi di rilis single-file / sesi non-interaktif — user cuma lihat taskbar kosong).</summary>
    private DateTime _startUtc = DateTime.UtcNow;
    private void EnsureVisibleAtStartup()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            // Hanya dalam ~6 dtk pertama — jangan ganggu kalau user sengaja minimize.
            if ((DateTime.UtcNow - _startUtc).TotalSeconds > 6) return;
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

            var r = new Rect(Left, Top, ActualWidth, ActualHeight);
            var vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (ActualWidth > 0 && !vs.IntersectsWith(r))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - ActualWidth) / 2;
                Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - ActualHeight) / 2;
            }
            Activate();
        }));
    }

    /* ---------------- Populasi statis ---------------- */

    private void PopulateSystem()
    {
        HeroModel.Text = SystemInfo.Model;
        HeroSub.Text = $"{SystemInfo.CpuName} · {SystemInfo.GpuName} · Live telemetry — diperbarui tiap {_sampleIntervalSec} detik.";
        TileCpuName.Text = CpuShort();
        CoolCpuName.Text = CpuShort();
        TileGpuName.Text = GpuShort();
        CoolGpuName.Text = GpuShort();
        LapModel.Text = SystemInfo.Model;
        LapOs.Text = SystemInfo.OsName;
        LapCpu.Text = SystemInfo.CpuName;
        LapGpu.Text = SystemInfo.GpuName;
        LapRam.Text = SystemInfo.RamTotalLabel;
        LapOsRow.Text = SystemInfo.OsName;
        LiveIv.Text = _sampleIntervalSec + " DTK";
        SetSW.IsChecked = _startWithWindows;
        SetTray.IsChecked = _minimizeToTray;
        SetIv.SelectedIndex = _sampleIntervalSec >= 5 ? 1 : 0;
        VerFoot.Text = "v" + AppInfo.VersionString;
        AboutVer.Text = "v" + AppInfo.VersionString + " · Universal laptop control center";
        UpVer.Text = AppInfo.VersionString;
        HkBox.Text = Hotkey.Format(_hMods, _hKey);
        UpRepo.Text = _updateRepo;
    }

    private string CpuShort() => SystemInfo.CpuName.Split(' ')[0];
    private string GpuShort() => SystemInfo.GpuName.Split(' ')[0];

    private void PopulateStorage()
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed && d.TotalSize > 0)
                .ToList();
            if (drives.Count == 0)
            {
                StoreEmpty.Visibility = Visibility.Visible;
                return;
            }

            var cards = new List<Border>();
            foreach (var d in drives)
            {
                double total = d.TotalSize / 1024d / 1024d / 1024d;
                double free = d.AvailableFreeSpace / 1024d / 1024d / 1024d;
                double used = Math.Max(0, total - free);
                double pct = total > 0 ? used / total * 100d : 0;

                var b = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 14) };
                var sp = new StackPanel { Margin = new Thickness(4, 2, 4, 2) };
                sp.Children.Add(new TextBlock { Style = (Style)FindResource("CardLabel"), Foreground = Brushes.White, Text = "VOLUME " + d.Name });

                var top = new Grid { Margin = new Thickness(0, 12, 0, 0) };
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var usedTxt = new TextBlock { FontSize = 24, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontWeight = FontWeights.Bold, Text = Math.Round(used).ToString() };
                top.Children.Add(usedTxt);
                top.Children.Add(new TextBlock { Text = " GB terpakai", VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4), Foreground = (Brush)FindResource("Txt2") });
                Grid.SetColumn(usedTxt, 0);
                top.Children.Add(new TextBlock
                {
                    Text = Math.Round(free) + " GB bebas · total " + Math.Round(total) + " GB",
                    Foreground = (Brush)FindResource("Txt3"),
                    FontSize = 12.5,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                Grid.SetColumn(top.Children[^1], 1);
                sp.Children.Add(top);

                var pb = new ProgressBar { Value = pct, Height = 16, Margin = new Thickness(0, 10, 0, 0), Foreground = (Brush)FindResource("Red") };
                sp.Children.Add(pb);

                var leg = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
                leg.Children.Add(new TextBlock { Text = "Terpakai " + Math.Round(pct) + "%", Foreground = (Brush)FindResource("Txt2"), FontSize = 12 });
                leg.Children.Add(new TextBlock { Text = "   Bebas", Foreground = (Brush)FindResource("Txt3"), FontSize = 12 });
                sp.Children.Add(leg);

                b.Child = sp;
                cards.Add(b);
            }
            foreach (var c in cards) StoreList.Items.Add(c);
        }
        catch { }
    }

    private void ReadPowerState()
    {
        try { if (NativePow.TryGetAc(out var a)) _acMode = a; } catch { }
        try { if (NativePow.TryGetDc(out var d)) _dcMode = d; } catch { }
    }

    /* ---------------- Navigasi ---------------- */

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is ListBoxItem item && item.Tag is string key)
            SelectPage(key);
    }

    private void SelectPage(string key)
    {
        SetVisible(PageDash, key == "dashboard");
        SetVisible(PagePower, key == "power");
        SetVisible(PageCool, key == "cooling");
        SetVisible(PageBat, key == "battery");
        SetVisible(PageLaptop, key == "mylaptop");
        SetVisible(PageStore, key == "storage");
        SetVisible(PageGame, key == "gaming");
        SetVisible(PageSet, key == "settings");
        SetVisible(PageAbout, key == "about");

        var p = Pages.First(x => x.Key == key);
        PgTitle.Text = p.Title;
        PgCrumb.Text = p.Crumb;

        UpdatePowerUI();
        UpdateCoolingUI();
    }

    private static void SetVisible(UIElement el, bool on) => el.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    /* ---------------- Shortcut global ---------------- */

    private void RegisterHotkey()
    {
        var h = _hwndSource?.Handle ?? IntPtr.Zero;
        if (h == IntPtr.Zero) return;
        Hotkey.UnregisterHotKey(h, Hotkey.HOTKEY_ID);
        bool ok = Hotkey.RegisterHotKey(h, Hotkey.HOTKEY_ID, _hMods, _hKey);
        if (!ok && !_hkWarned)
        {
            _hkWarned = true;
            MessageBox.Show("Shortcut '" + Hotkey.Format(_hMods, _hKey) +
                "' gagal didaftarkan (dipakai aplikasi lain?). Coba kombinasi lain.",
                "AllSense", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyHotkey(uint mods, uint key)
    {
        const uint anyMod = Hotkey.MOD_CONTROL | Hotkey.MOD_ALT | Hotkey.MOD_SHIFT | Hotkey.MOD_WIN;
        if ((mods & anyMod) == 0)
        {
            MessageBox.Show("Pakai minimal satu modifier (Ctrl/Alt/Shift/Win).",
                "AllSense", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var h = _hwndSource?.Handle ?? IntPtr.Zero;
        if (h != IntPtr.Zero)
        {
            Hotkey.UnregisterHotKey(h, Hotkey.HOTKEY_ID);
            if (!Hotkey.RegisterHotKey(h, Hotkey.HOTKEY_ID, mods, key))
            {
                Hotkey.RegisterHotKey(h, Hotkey.HOTKEY_ID, _hMods, _hKey);  // rollback
                MessageBox.Show("Kombinasi '" + Hotkey.Format(mods, key) + "' tidak bisa dipakai (bentrok).",
                    "AllSense", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        _hMods = mods;
        _hKey = key;
        HkBox.Text = Hotkey.Format(_hMods, _hKey);
        SaveSettings();
    }

    private void HkBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        uint mods = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= Hotkey.MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= Hotkey.MOD_SHIFT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= Hotkey.MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= Hotkey.MOD_WIN;
        if (k is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(k);
        if (vk == 0) return;
        ApplyHotkey(mods, vk);
    }

    private void HkReset_Click(object sender, RoutedEventArgs e)
        => ApplyHotkey(Hotkey.MOD_CONTROL | Hotkey.MOD_ALT, 0x41);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Hotkey.WM_HOTKEY && wParam.ToInt32() == Hotkey.HOTKEY_ID)
        {
            BringToFront();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void BringToFront()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Hotkey.SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    /* ---------------- Update via GitHub ---------------- */

    private async void UpCheck_Click(object sender, RoutedEventArgs e)
    {
        _updateRepo = UpRepo.Text.Trim();
        if (string.IsNullOrWhiteSpace(_updateRepo)) { _updateRepo = AppUpdate.DefaultRepo; UpRepo.Text = _updateRepo; }
        SaveSettings();
        UpCheck.IsEnabled = false;
        UpStatus.Text = "Memeriksa…";
        UpBtn.Visibility = Visibility.Collapsed;
        try
        {
            var r = await AppUpdate.CheckAsync(_updateRepo);
            _lastUpdate = r;
            if (!r.Ok)
                UpStatus.Text = r.Error ?? "Gagal memeriksa update.";
            else if (r.Latest! <= AppInfo.AssemblyVersion)
            {
                UpStatus.Text = "Sudah versi terbaru (" + AppInfo.VersionString + ").";
                UpBtn.Visibility = Visibility.Collapsed;
                UpBadge.Visibility = Visibility.Collapsed;
            }
            else
            {
                UpStatus.Text = "Update tersedia: " + r.Tag + ".";
                UpBtn.Content = "Unduh & pasang " + r.Tag;
                UpBtn.Visibility = Visibility.Visible;
                UpBadge.Content = "Update " + r.Tag + " · klik";
                UpBadge.Visibility = Visibility.Visible;
            }
        }
        finally { UpCheck.IsEnabled = true; }
    }

    private async void UpBtn_Click(object sender, RoutedEventArgs e)
        => await DoInstallAsync(_lastUpdate);

    private async void UpBadge_Click(object sender, RoutedEventArgs e)
    {
        UpBadge.Visibility = Visibility.Collapsed;
        await DoInstallAsync(_lastUpdate);
    }

    private async Task DoInstallAsync(AppUpdate.Result? r)
    {
        if (r == null || string.IsNullOrEmpty(r.AssetUrl)) { UpBtn.IsEnabled = true; return; }
        UpBtn.IsEnabled = false;
        UpStatus.Text = "Mengunduh " + r.AssetName + "…";
        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "AllSense-update");
            string zip = await AppUpdate.DownloadAsync(r.AssetUrl!, dir);
            UpStatus.Text = "Memasang… AllSense akan ditutup lalu restart.";
            AppUpdate.Install(zip, Environment.ProcessPath!);
            _trayExitRequested = true;   // paksa keluar (jangan sembunyi ke tray)
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
        catch (Exception ex)
        {
            UpStatus.Text = "Gagal memasang update: " + ex.Message;
            UpBtn.IsEnabled = true;
            UpBadge.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Cek update otomatis saat start — senyap kalau gagal / sudah terbaru.</summary>
    private async void AutoCheckUpdate()
    {
        try
        {
            var r = await AppUpdate.CheckAsync(_updateRepo);
            _lastUpdate = r;
            if (!r.Ok || r.Latest! <= AppInfo.AssemblyVersion) return;

            UpBadge.Content = "Update " + r.Tag + " · klik";
            UpBadge.Visibility = Visibility.Visible;

            // sinkronkan halaman Settings juga
            UpStatus.Text = "Update tersedia: " + r.Tag + ".";
            UpBtn.Content = "Unduh & pasang " + r.Tag;
            UpBtn.Visibility = Visibility.Visible;
        }
        catch { }
    }

    /* ---------------- Loop live ---------------- */

    // Kumpulkan data berat (sensor LHM + WMI) di thread belakang; cuma update UI di dispatcher.
    private void Tick()
    {
        if (_polling || _closed) return;
        _polling = true;

        var clock = DateTime.Now.ToString("HH:mm:ss");
        Task.Run(() =>
        {
            Snapshot s = new();
            try { _hw.Poll(); } catch { }
            s.Avail = _hw.Available;
            s.CpuT = _hw.CpuTemperatureC;
            s.GpuT = _hw.GpuTemperatureC;
            s.CpuL = _hw.CpuLoadPercent;
            s.GpuL = _hw.GpuLoadPercent;
            s.Fan = _hw.FanRpm;
            (s.Used, s.Total) = NativePow.GetMemoryGB();
            s.Ac = NativePow.IsOnAC();
            s.Bat = NativePow.GetBatteryInfo();

            try
            {
                Dispatcher.BeginInvoke(() => ApplySnapshot(s, clock));
            }
            finally { _polling = false; }
        });
    }

    private sealed class Snapshot
    {
        public bool Avail; public float CpuT, GpuT, CpuL, GpuL, Fan;
        public double Used, Total; public bool? Ac;
        public (int? ChargePercent, int? BatteryStatus, int? LifeSeconds, int? FullLifeSeconds) Bat;
    }

    private void ApplySnapshot(Snapshot s, string clock)
    {
        if (_closed) return;
        double ramPct = s.Total > 0 ? s.Used / s.Total * 100d : 0;

        ClockText.Text = clock;
        ChipSensor.Text = s.Avail ? "Sensor aktif" : "Sensor idle";
        ChipSrc.Text = s.Ac == null ? "—" : s.Ac.Value ? "AC" : "Baterai";

        // Dashboard gauges — SISTEM/GPU tampilkan SUHU (selaras halaman Cooling); bar = beban.
        CpuPct.Text = Fmt(s.CpuT) + "°C";
        GpuPct.Text = Fmt(s.GpuT) + "°C";
        SetGauge(GaugeRam, (float)ramPct, null);
        SetGauge(GaugeBat, s.Bat.ChargePercent ?? 0, null);
        BarCpu.Value = Clamp(s.CpuL);
        BarGpu.Value = Clamp(s.GpuL);
        BarRam.Value = Clamp(ramPct);
        BarBat.Value = Clamp(s.Bat.ChargePercent ?? 0);

        TileCpuSub.Text = Fmt(s.CpuT) + "°C · " + (_hw.CpuViaCore ? "inti" : "zona ACPI");
        TileGpuSub.Text = Fmt(s.GpuT) + "°C · core";
        TileRamSub.Text = s.Used.ToString("0.#") + " GB terpakai";
        TileBatSub.Text = Fmt(s.Bat.ChargePercent) + "% · " + (s.Ac == false ? "Baterai" : "AC");

        // Battery page
        BatBig.Value = Clamp(s.Bat.ChargePercent ?? 0);
        BatBig.DisplayText = Fmt(s.Bat.ChargePercent);
        BatState.Text = s.Ac == false ? "Sedang menguras" : "Terisi";
        BatSub.Text = "Sumber: " + (s.Ac == null ? "—" : s.Ac.Value ? "AC" : "Baterai");
        BStatus.Text = s.Ac == false ? "Menguras" : s.Ac == true ? "Terhubung AC" : "—";
        BSrc.Text = s.Ac == null ? "—" : s.Ac.Value ? "AC" : "Baterai";
        BLife.Text = s.Bat.LifeSeconds is int ls && ls > 0 ? TimeSpan.FromSeconds(ls).ToString(@"h\:mm") : "—";
        BTemp.Text = Fmt(s.CpuT) + "°C";

        // Cooling page
        CoolCpuPct.Text = Fmt(s.CpuT) + "°C";
        CoolCpuSub.Text = Fmt(s.CpuT) + "°C · " + (_hw.CpuViaCore ? "inti" : "zona ACPI");
        CoolBarCpu.Value = PctTemp(s.CpuT);
        CoolGpuPct.Text = Fmt(s.GpuT) + "°C";
        CoolGpuSub.Text = Fmt(s.GpuT) + "°C · core";
        CoolBarGpu.Value = PctTemp(s.GpuT);
        FanRpm.Text = Fmt(s.Fan);
        TrCpu.Text = Fmt(s.CpuT) + "°C";
        TrGpu.Text = Fmt(s.GpuT) + "°C";
        TrCpuBar.Value = PctTemp(s.CpuT);
        TrGpuBar.Value = PctTemp(s.GpuT);

        UpdatePowerUI();
    }

    private static double Clamp(float v) => float.IsNaN(v) ? 0 : Math.Clamp(v, 0, 100);
    private static double Clamp(double v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 100);
    private static double PctTemp(float t) => float.IsNaN(t) ? 0 : Math.Clamp((t - 20) * 100 / 80, 0, 100);
    private static string Fmt(float f) => float.IsFinite(f) ? Math.Round(f).ToString("0") : "—";
    private static string Fmt(int? n) => n is int v ? v.ToString() : "—";

    private static void SetGauge(RingGauge g, float v, string? text)
    {
        g.Value = Clamp(v);
        g.DisplayText = text;
    }

    /* ---------------- Power ---------------- */

    private static string ModeName(PowerMode m) => m switch
    {
        PowerMode.Efficiency => "Efisiensi",
        PowerMode.Performance => "Performa",
        _ => "Balanced",
    };

    private static string ModeTag(PowerMode m) => m switch
    {
        PowerMode.Efficiency => "Best Power Efficiency",
        PowerMode.Performance => "Best Performance",
        _ => "Balanced",
    };

    private static string[] ModeInfo(PowerMode m) => m switch
    {
        PowerMode.Efficiency => new[] { "CPU boost dihemat — kipas tenang", "Kecerahan layar sedikit diturunkan", "Kecepatan dikurangi → daya tahan maksimal" },
        PowerMode.Performance => new[] { "CPU boost penuh, frame rate tinggi", "Kipas agresif untuk pendinginan ekstrem", "Konsumsi daya lebih tinggi saat colokan" },
        _ => new[] { "Seimbang antara performa dan daya", "Opsi default Windows — cocok harian", "Menyesuaikan otomatis per beban kerja" },
    };

    private void Seg_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string key)
        {
            var mode = KeyToMode(key);
            ApplyPower(mode);
        }
    }

    private void ApplyPower(PowerMode mode)
    {
        bool modA = false, modD = false;
        try { modA = NativePow.SetAc(mode); } catch { }
        try { modD = NativePow.SetDc(mode); } catch { }

        bool ok;
        if (modA && modD) { ok = true; _method = "modern"; }
        else
        {
            ok = NativePow.SetActiveScheme(mode);
            _method = ok ? "scheme" : "failed";
        }

        if (ok)
        {
            _acMode = mode;
            if (_linked) _dcMode = mode;
        }
        ReadPowerState();
        UpdatePowerUI();

        if (!ok)
        {
            MessageBox.Show("Gagal menerapkan mode daya.", "AllSense", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Link_Changed(object sender, RoutedEventArgs e)
    {
        _linked = LinkACDC.IsChecked == true;
        if (_linked) _dcMode = _acMode;
        UpdatePowerUI();
    }

    private void UpdatePowerUI()
    {
        bool eff = _acMode == PowerMode.Efficiency;
        bool bal = _acMode == PowerMode.Balanced;
        bool perf = _acMode == PowerMode.Performance;
        StyleSeg(SegEff, eff, SegEffBadge);
        StyleSeg(SegBal, bal, SegBalBadge);
        StyleSeg(SegPerf, perf, SegPerfBadge);

        PwMode.Text = ModeTag(_acMode);
        PwAc.Text = ModeName(_acMode);
        PwDc.Text = ModeName(_dcMode);
        PwLink.Text = _linked ? "TERKUNCI" : "TERPISAH";
        PwMth.Text = _method.ToUpperInvariant();
        PowerInfo.Text = string.Join("\n", ModeInfo(_acMode));
        HeroMode.Text = ModeName(_acMode) + " — AC & DC";
        LinkState.Text = _linked ? "TERKUNCI" : "TERPISAH";
        LinkACDC.IsChecked = _linked;

        GameMode.Text = "Mode daya saat gaming — " + ModeName(_acMode) + ".";
        GameModeRow.Text = ModeName(_acMode);
    }

    private static void StyleSeg(Button b, bool on, TextBlock badge)
    {
        b.BorderBrush = on ? (Brush)Application.Current.FindResource("Red") : (Brush)Application.Current.FindResource("Line");
        b.Background = on ? new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x2D, 0x4D)) : (Brush)Application.Current.FindResource("Panel2");
        badge.Foreground = on ? (Brush)Application.Current.FindResource("Red") : (Brush)Application.Current.FindResource("Txt3");
    }

    private void UpdateCoolingUI() => CoolStatus.Text = _hw.Available ? "OK" : "IDLE";

    private static PowerMode KeyToMode(string key) => key switch
    {
        "eff" => PowerMode.Efficiency,
        "perf" => PowerMode.Performance,
        _ => PowerMode.Balanced,
    };

    /* ---------------- Aksi cepat ---------------- */

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string target) return;
        string cmd = target switch
        {
            "display" => "ms-settings:display",
            "sound" => "mmsys.cpl",
            "network" => "ms-settings:network-wifi",
            "power" => "ms-settings:powersleep",
            "storage" => "ms-settings:storagesense",
            "taskmgr" => "taskmgr.exe",
            "settings" => "ms-settings:",
            _ => "ms-settings:",
        };
        try { Process.Start(new ProcessStartInfo { FileName = cmd, UseShellExecute = true }); } catch { }
    }

    /* ---------------- Pengaturan persist ---------------- */

    private void SetSW_Changed(object sender, RoutedEventArgs e)
    {
        _startWithWindows = SetSW.IsChecked == true;
        ApplyStartWithWindows(_startWithWindows);
        SaveSettings();
    }

    private void SetTray_Changed(object sender, RoutedEventArgs e)
    {
        _minimizeToTray = SetTray.IsChecked == true;
        if (_minimizeToTray) SetupTray(); else DisposeTray();
        SaveSettings();
    }

    private void SetIv_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SetIv.SelectedItem is ComboBoxItem item && item.Tag is string s && int.TryParse(s, out var sec))
        {
            sec = Math.Max(2, sec);
            _sampleIntervalSec = sec;
            _timer.Interval = TimeSpan.FromSeconds(sec);
            LiveIv.Text = sec + " DTK";
            SaveSettings();
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsFile)) return;
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsFile));
            if (s == null) return;
            _startWithWindows = s.StartWithWindows;
            _minimizeToTray = s.MinimizeToTray;
            _sampleIntervalSec = s.SampleIntervalSec >= 2 ? s.SampleIntervalSec : 2;
            _hMods = (uint)s.HotkeyModifiers;
            _hKey = (uint)s.HotkeyKey;
            _updateRepo = string.IsNullOrWhiteSpace(s.UpdateRepo) ? AppUpdate.DefaultRepo : s.UpdateRepo!;
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(_settingsDir);
            var s = new Settings
            {
                StartWithWindows = _startWithWindows,
                MinimizeToTray = _minimizeToTray,
                SampleIntervalSec = _sampleIntervalSec,
                HotkeyModifiers = (int)_hMods,
                HotkeyKey = (int)_hKey,
                UpdateRepo = _updateRepo,
            };
            File.WriteAllText(_settingsFile, JsonSerializer.Serialize(s));
        }
        catch { }
    }

    private void ApplyStartWithWindows(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;
            if (enabled) key.SetValue("AllSense", "\"" + Environment.ProcessPath + "\"");
            else key.DeleteValue("AllSense", throwOnMissingValue: false);
        }
        catch { }
    }

    /* ---------------- Tray ---------------- */

    private void SetupTray()
    {
        if (_trayIcon != null) return;
        var tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "AllSense Control",
            Visible = true,
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Tampilkan", null, (_, _) => ShowFromTray());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Keluar", null, (_, _) => { _trayExitRequested = true; Close(); });
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowFromTray();
        _trayIcon = tray;
    }

    private void DisposeTray()
    {
        if (_trayIcon == null) return;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_minimizeToTray && !_trayExitRequested)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    /* ---------------- Title bar gelap ---------------- */

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void TrySetDarkTitleBar()
    {
        try
        {
            if (IsInitialized)
            {
                var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (h != IntPtr.Zero)
                {
                    int on = 1;
                    DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
                }
            }
        }
        catch { }
    }
}
