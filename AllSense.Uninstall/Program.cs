using System;
using System.Diagnostics;
using System.IO;

namespace AllSense.Uninstall;

/// <summary>
/// Uninstaller minimal per-user: hapus shortcut, kunci registry, lalu tunda hapus
/// isi direktori + diri sendiri lewat cmd background (rmdir bisa setelah proses ini keluar).
/// Dipanggil dari entri UninstallString: "AllSense.Uninstall.exe" --uninstall [dir].
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--uninstall") < 0) return 0;

        string dir = "";
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--uninstall") { dir = args[i + 1]; break; }
        if (string.IsNullOrWhiteSpace(dir))
        {
            try
            {
                if (Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\AllSense",
                    "InstallLocation", null) is string loc) dir = loc;
            }
            catch { }
        }

        // Shortcut Start Menu.
        try
        {
            string lnk = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs", "AllSense Control.lnk");
            if (File.Exists(lnk)) File.Delete(lnk);
        }
        catch { }

        // Kunci uninstall registry.
        try
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AllSense", false);
        }
        catch { }

        // Hapus direktori + diri sendiri setelah 2 dtk (proses ini sudah keluar → file tak terkunci).
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
        {
            string self = Environment.ProcessPath ?? "";
            string dirQ = "\"" + dir.Replace("\"", "") + "\"";
            string selfQ = "\"" + self.Replace("\"", "") + "\"";
            string cmd = "/c timeout /t 2 /nobreak >nul & rmdir /s /q " + dirQ +
                         " & reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AllSense\" /f >nul 2>&1 & del /q " + selfQ + " >nul 2>&1";
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", cmd)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            catch { }
        }
        return 0;
    }
}
