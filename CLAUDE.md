# AllSense Control — kartu proyek

Universal laptop control center (Windows). WPF/.NET 8, x64.
Fitur inti: Power Mode (AC+DC, satu klik) + suhu live CPU/GPU/RAM.

## Lokasi
- App: `AllSense.App/`
- Distribusi: `AllSense.App/dist/AllSense-v1.0-x64/` (+ `.zip`)
- Agent: `.claude/agents/allsense-developer.md`

## Build & uji
```
& "$HOME\.dotnet\dotnet.exe" build "C:\Users\aflin\Music\All Sense\AllSense.App\AllSense.App.csproj" -c Release
# selftest: exit 0 = lolos; marker & report di %TEMP%\AllSense-selftest\
```

## Status terverifikasi (dari self-test nyata, exit 0)
- power.ok, power_modern.ok, perf_roundtrip.ok, power_read.ok.
- Dua keluarga GUID: **mode slider** (`PowerSetUserConfigured{AC,DC}PowerMode`) → Efficiency `961cc777-2547-4f9d-8174-7d86181b8a7a`, Balanced `00000000-0000-0000-0000-000000000000`, Performance `ded574b5-45a0-4f42-8737-46345c09c238`; **skema** (`PowerSetActiveScheme`) → `a4f06079…/381b4222…/8c5e7fda…`. JANGAN saling tukar (GUID skema di API mode → 87). Kini API modern sukses; fallback skema dijaga untuk mesin legacy.
- Suhu "CPU" = **suhu mainboard/sistem dari zona termal ACPI** (`Win32_PerfFormattedData_Counters_ThermalZoneInformation` / `MSAcpi_ThermalZoneTemperature`, ~28°C idle). LHM core CPU (MSR) **tidak dipakai** — butuh driver ring0 yang tak selalu bisa dimuat (Secure Boot). UI label "SUHU SISTEM"/chip "ACPI".
- Suhu GPU = **sensor inti** (nama mengandung "core", mis. NVIDIA `GPU Core`). JANGAN ambil max semua sensor suhu — `GPU Hot Spot`/`GPU Memory Junction` jauh lebih panas (~61/72°C vs core ~54°C) dan bukan yang dibandingkan dgn Task Manager. Waspada `Math.Max(gpu, NaN)=NaN` saat GPU kedua (iGPU) tanpa sensor suhu — guard `!float.IsNaN(tg)` dulu.
- Halaman Cooling: grid atasan 2×2 (`UniformGrid Columns="2"`) — 4 kolom sama lebar bikin kepotong di <1180px. Baris label+value pakai `ColumnDefinitions (*,Auto)` — tanpa kolom dua TextBlock numpuk & overlap.
- **Tanpa admin**: `app.manifest` → `requestedExecutionLevel=asInvoker` (tanpa UAC). Tidak perlu elevation lagi karena core CPU tak disentuh.
- cpu_sensor.ok, gpu_sensor.ok, memory.ok, storage.ok, wmi.ok.
- Storage: judul volume dinamis mengikuti drive yang terbaca.
- Publish self-contained single-file x64: hidup, tanpa crash. Selftest `--selftest` exit 0.
- **UI native WPF/XAML** (tanpa WebView2): `MainWindow.xaml` + code-behind, pakai style `App.xaml` + `RingGauge`. Buang WebView2 → memori turun dari ±975 MB (WebView2 24 proses ≈800 MB) ke ±200 MB WS / +130 MB private, tanpa runtime WebView2, start lebih cepat. `Web/design.html` sudah tak dipakai (referensi dihapus).
- **Polling di thread belakang**: `Tick()` hanya scheduling; `_hw.Poll()` + WMI jalan di `Task.Run`, update UI via `Dispatcher.BeginInvoke`. Guard `_polling` cegah tumpang-tindih. UI tidak membeku walau WMI lambat.
- **Crash log**: `App.xaml.cs` menangkap `DispatcherUnhandledException`/`UnhandledException` → tulis `%TEMP%\AllSense-selftest\crash.log`. Startup crash dulu karena `LinkACDC IsChecked="True"` di XAML memicu `Link_Changed` sebelum kontrol lain dibuat; fix: set IsChecked=false di XAML, `_linked` diatur di code. Dark title bar via `SourceInitialized` → `TrySetDarkTitleBar`.
- **Shortcut global**: `Hotkey` (NativeInterop.cs) pakai `RegisterHotKey` (ID 0xE100, WM_HOTKEY via `HwndSource.AddHook`). Default Ctrl+Alt+A; bisa di-custom di Settings (kotak tangkap `HkBox`) → `ApplyHotkey` daftar ulang + `SaveSettings`. Minimal 1 modifier; gagal daftar (bentrok) → rollback + warning sekali. `BringToFront` = Show + Activate + `SetForegroundWindow` (bawa ke depan walau di tray).
- **Update via GitHub** (`AppUpdate.cs`): tombol "Cek update" manual (bukan auto) di Settings. `CheckAsync` → `api.github.com/repos/{owner/repo}/releases/latest`, ambil semver stabil (abaikan suffix prerelease via `TryParseTag`), asset `.zip` prefer nama x64. `Install` ekstrak ke `%TEMP%\AllSense-update\new`, tulis `updater.bat` yang menunggu PID mati, `copy /Y` timpa exe lama (`Environment.ProcessPath`), restart, hapus diri. `_trayExitRequested=true` sebelum `Close()` agar beneran keluar (bukan sembunyi tray). Repo **wajib diset** di Settings; `AppInfo.VersionString` baca dari assembly (csproj `Version=1.2.0`).
- Selftest tambahan: `version.ok`, `update_tagparse.ok`, `hotkey_format.ok` (semua lolos, `overall` exit 0).

Lihat agent `allsense-developer` untuk detail teknis & aturan.
