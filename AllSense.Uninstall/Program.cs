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

        // Hapus direktori + diri sendiri setelah 2 dtk, dengan retry (proses ini sudah keluar
        // → file tak terkunci). Pakai PowerShell: lebih andal daripada rmdir (retry + force).
        if (!string.IsNullOrWhiteSpace(dir))
        {
            string self = Environment.ProcessPath ?? "";
            string dirEsc = dir.Replace("'", "''");
            string selfEsc = self.Replace("'", "''");
            // Hapus tiap file dgn retry (AV/Defender sering kunci exe besar beberapa dtk).
            string ps =
                "Start-Sleep -Seconds 5; Set-Location $env:TEMP; " +
                "$ok=$false; if(Test-Path -LiteralPath '##DIR##'){ " +
                "  Get-ChildItem -LiteralPath '##DIR##' -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { $f=$_.FullName; $j=0; while($j -lt 30){ try { Remove-Item -LiteralPath $f -Force -ErrorAction Stop; break } catch { Start-Sleep -Milliseconds 1000; $j++ } } }; " +
                "  for($i=0;$i -lt 10 -and -not $ok;$i++){ try { Remove-Item -LiteralPath '##DIR##' -Recurse -Force -ErrorAction Stop; $ok=$true } catch { Start-Sleep -Seconds 1 } } " +
                "}; " +
                "reg delete 'HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AllSense' /f | Out-Null; " +
                "del '##SELF##' | Out-Null";
            ps = ps.Replace("##DIR##", dirEsc).Replace("##SELF##", selfEsc);
            try
            {
                var psi = new ProcessStartInfo("powershell.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-WindowStyle");
                psi.ArgumentList.Add("Hidden");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add(ps);
                Process.Start(psi);
            }
            catch { }
        }
        return 0;
    }
}
