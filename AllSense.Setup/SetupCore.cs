using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace AllSense.Setup;

/// <summary>Logika inti installer yang murni & dapat diuji (tanpa UI/COM).</summary>
public static class SetupCore
{
    public const string AppExe = "AllSense.App.exe";
    public const string UninstallExe = "AllSense.Uninstall.exe";
    public const string DisplayVersion = "1.3.2";

    public static string DefaultInstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "AllSense");

    /// <summary>Decompress stream gzip ke berkas tujuan (timpa).</summary>
    public static void DecompressToFile(Stream gzSource, string dest)
    {
        using var gz = new GZipStream(gzSource, CompressionMode.Decompress);
        using var fs = File.Create(dest);
        gz.CopyTo(fs);
        fs.Flush(true);
    }

    /// <summary>Cek penanda PE: 'MZ' di awal + ukuran masuk akal (&gt;1MB).</summary>
    public static bool IsValidPe(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (fi.Length < 1_000_000) return false;
            using var fs = File.OpenRead(path);
            if (fs.ReadByte() != (byte)'M' || fs.ReadByte() != (byte)'Z') return false;
            return true;
        }
        catch { return false; }
    }

    /// <summary>Reservasi ruang disk minimum untuk instal (app + uninstaller + toleransi).</summary>
    public const long MinFreeBytesForInstall = 600L * 1024 * 1024;

    /// <summary>Apakah folder bisa ditulis (uji tulis + hapus berkas probe).</summary>
    public static bool IsWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, ".allsense-write-probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Apakah ada ruang kosong cukup di drive folder target.</summary>
    public static bool HasEnoughDiskSpace(string dir, long requiredBytes)
    {
        try
        {
            var drv = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir)));
            return drv.AvailableFreeSpace >= requiredBytes;
        }
        catch { return true; }   // tak bisa cek → jangan blokir
    }

    /// <summary>Apakah proses app AllSense sedang berjalan (cegah menimpa exe terkunci).</summary>
    public static bool IsAllSenseRunning()
    {
        try { return Process.GetProcessesByName("AllSense.App").Length > 0; }
        catch { return false; }
    }

    /// <summary>Hentikan semua proses AllSense; mengembalikan true bila tuntas.</summary>
    public static bool StopAllSenseRunning()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("AllSense.App"))
            {
                try { p.Kill(); p.WaitForExit(5000); } catch { }
            }
            return !IsAllSenseRunning();
        }
        catch { return false; }
    }

    /// <summary>Label arsitektur proses saat ini (untuk komunikasi & penolakan jelas).</summary>
    public static string ArchitectureLabel => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "ARM64",
        Architecture.X86 => "x86 (32-bit)",
        _ => "tidak dikenal",
    };
}
