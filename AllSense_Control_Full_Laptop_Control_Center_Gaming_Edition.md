# ALLSENSE CONTROL - PROYEK SIMPUL (Scaffold)

## 1. Persiapan
- Pastikan Anda memiliki **.NET 8 SDK** terinstal (download dari https://aka.ms/dotnet)
- Windows 11 (64-bit) diperlukan untuk WinUI 3

## 2. Struktur Proyek
```
AllSenseControl/
├── AllSenseControl.sln
├── AllSense.Core/
│   ├── AllSense.Core.csproj
│   ├── Models/
│   │   ├── DeviceIdentity.cs
│   │   └── ...
│   ├── Contracts/
│   │   ├── IDeviceInformationProvider.cs
│   │   └── ...
│   └── Helpers/
├── AllSense.Windows/
│   ├── AllSense.Windows.csproj
│   └── ...
└── AllSense.UI/
    ├── AllSense.UI.csproj
    └── ...
```

## 3. Langkah Awal
### 3.1 Buat solution dan modul utama
```powershell
mkdir C:\Users\aflin\Music\All Sense\AllSenseControl
cd C:\Users\aflin\Music\All Sense\AllSenseControl
mkdir AllSense.Core
mkdir AllSense.Windows
mkdir AllSense.UI
```

### 3.2 Buat file proyek AllSense.Core
```powershell
cd AllSense.Core
dotnet new classlib -n AllSense.Core
```

### 3.3 Buat file proyek AllSense.UI
```powershell
cd ..\AllSense.UI
dotnet new wpf -n AllSense.UI -f net8.0-windows
```

## 4. Koneksi ke Windows API
- Gunakan `powrprof.dll` untuk kontrol mode listrik
- Gunakan `GetSystemPowerStatus` untuk AC/DC status
- Gunakan `GlobalMemoryStatusEx` untuk memori

## 5. Langkah Selanjutnya
- Tambahkan referensi ke `AllSense.Core` di `AllSense.Windows`
- Implementasi dashboard, monitoring, dan fitur power center
- Ikuti roadmap Phase 1 dari dokumen

## 6. Catatan Penting
- Tidak ada permission SYSTEM diperlukan di awal
- Semua fitur harus memakai API Windows resmi
- Fitur hardware khusus (fan, RGB) hanya untuk perangkat yang mendukung