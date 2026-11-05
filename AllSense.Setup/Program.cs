using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

namespace AllSense.Setup;

internal static class Program
{
    internal const string AppExe = SetupCore.AppExe;
    internal const string UninstallExe = SetupCore.UninstallExe;
    internal const string DisplayVersion = SetupCore.DisplayVersion;

    [STAThread]
    private static int Main(string[] args)
    {
        // Mode senyap untuk riset/uji: --silent <dir>
        int sIdx = Array.IndexOf(args, "--silent");
        if (sIdx >= 0 && sIdx + 1 < args.Length)
        {
            try { Install(args[sIdx + 1], quiet: true); return 0; }
            catch { return 1; }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new InstallerForm());
        return 0;
    }

    internal static void Install(string installDir, bool quiet)
    {
        Log("mulai install ke '" + installDir + "'");

        if (string.IsNullOrWhiteSpace(installDir)) throw new InvalidOperationException("Folder pemasangan kosong.");

        // Preflight 1: arsitektur — installer ini dibangun untuk Windows x64.
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            throw new InvalidOperationException("AllSense hanya mendukung Windows.");
        if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            throw new InvalidOperationException("Installer ini untuk Windows x64; sistem Anda terdeteksi " + SetupCore.ArchitectureLabel + ".");

        // Preflight 2: ruang disk.
        if (!SetupCore.HasEnoughDiskSpace(installDir, SetupCore.MinFreeBytesForInstall))
            throw new InvalidOperationException("Ruang disk tidak cukup untuk memasang AllSense.");

        // Preflight 3: jangan menimpa exe yang sedang berjalan.
        if (SetupCore.IsAllSenseRunning())
        {
            if (quiet)
            {
                throw new InvalidOperationException(
                    "AllSense sedang berjalan. Tutup aplikasinya terlebih dahulu, lalu ulangi pemasangan.");
            }
            var r = MessageBox.Show(
                "AllSense sedang berjalan. Tutup sekarang agar dapat memasang versi terbaru?",
                "AllSense", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes || !SetupCore.StopAllSenseRunning())
                throw new InvalidOperationException("Pemasangan dibatalkan: AllSense masih berjalan.");
        }

        Log("preflight ok; ekstrak payload");

        Directory.CreateDirectory(installDir);

        // Preflight 2b: pastikan folder target benar-benar bisa ditulis.
        if (!SetupCore.IsWritable(installDir))
            throw new InvalidOperationException("Folder pemasangan tidak bisa ditulis: " + installDir);

        ExtractApp(installDir);
        Extract(UninstallExe, installDir);

        string appPath = Path.Combine(installDir, AppExe);

        Log("shortcut & registry (non-fatal)");

        // Shortcut Start Menu (per-user) — non-fatal: bila COM WScript.Shell diblokir
        // kebijakan, lanjutkan; isi utama (exe) sudah terpasang.
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is not null)
            {
                dynamic shell = Activator.CreateInstance(shellType);
                if (shell is not null)
                {
                    try
                    {
                        string lnk = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                            "Programs", "AllSense Control.lnk");
                        dynamic sc = shell.CreateShortcut(lnk);
                        sc.TargetPath = appPath;
                        sc.WorkingDirectory = installDir;
                        sc.IconLocation = appPath + ",0";
                        sc.Description = "AllSense Control";
                        sc.Save();
                    }
                    finally
                    {
                        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                    }
                }
            }
        }
        catch (Exception ex) { Log("shortcut gagal (diabaikan): " + ex.Message); }

        // Entri Uninstall (Apps & Features per-user) — non-fatal.
        try
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: true))
            using (var sub = key?.CreateSubKey("AllSense"))
            {
                if (sub is not null)
                {
                    sub.SetValue("DisplayName", "AllSense Control");
                    sub.SetValue("DisplayVersion", DisplayVersion);
                    sub.SetValue("Publisher", "Maxodus126");
                    sub.SetValue("InstallLocation", installDir);
                    sub.SetValue("UninstallString", "\"" + Path.Combine(installDir, UninstallExe) + "\" --uninstall");
                    sub.SetValue("QuietUninstallString", "\"" + Path.Combine(installDir, UninstallExe) + "\" --uninstall");
                    sub.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    sub.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
        }
        catch (Exception ex) { Log("registry gagal (diabaikan): " + ex.Message); }

        Log("instal selesai");

        if (!quiet)
        {
            if (MessageBox.Show("AllSense Control terpasang.\nJalankan sekarang?", "AllSense",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(appPath) { UseShellExecute = true });
            }
        }
    }

    /// <summary>Append ke %TEMP%\AllSense-install.log utk diagnosa jarak jauh bila gagal.</summary>
    private static readonly object LogLock = new();
    private static void Log(string msg)
    {
        try
        {
            lock (LogLock)
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "AllSense-install.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\r\n");
            }
        }
        catch { }
    }

    /// <summary>Ekstrak app utama dari payload terkompres (gzip), lalu validasi berkas exe (PE).</summary>
    private static void ExtractApp(string dir)
    {
        using var rs = Assembly.GetExecutingAssembly().GetManifestResourceStream("AllSense.App.pkg.gz")
            ?? throw new InvalidOperationException("Payload aplikasi tidak ditemukan.");
        string dest = Path.Combine(dir, AppExe);
        SetupCore.DecompressToFile(rs, dest);
        if (!SetupCore.IsValidPe(dest))
            throw new InvalidOperationException("Berkas aplikasi hasil ekstrak tidak valid (gagal/rusak).");
    }

    private static void Extract(string name, string dir)
    {
        using var rs = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("File " + name + " tidak ditemukan dalam installer.");
        using var fs = File.Create(Path.Combine(dir, name));
        rs.CopyTo(fs);
    }
}
