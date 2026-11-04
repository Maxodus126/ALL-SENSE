using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace AllSense.Setup;

internal static class Program
{
    internal const string AppExe = "AllSense.App.exe";
    internal const string UninstallExe = "AllSense.Uninstall.exe";
    internal const string DisplayVersion = "1.3.1";

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
        if (string.IsNullOrWhiteSpace(installDir)) throw new InvalidOperationException("Folder pemasangan kosong.");
        Directory.CreateDirectory(installDir);

        Extract(AppExe, installDir);
        Extract(UninstallExe, installDir);

        string appPath = Path.Combine(installDir, AppExe);

        // Shortcut Start Menu (per-user).
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"))!;
        dynamic sc;
        try
        {
            string lnk = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs", "AllSense Control.lnk");
            sc = shell.CreateShortcut(lnk);
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

        // Entri Uninstall (Apps & Features per-user).
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: true) ?? throw new InvalidOperationException("Registry tidak bisa diakses."))
        using (var sub = key.CreateSubKey("AllSense"))
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

        if (!quiet)
        {
            if (MessageBox.Show("AllSense Control terpasang.\nJalankan sekarang?", "AllSense",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(appPath) { UseShellExecute = true });
            }
        }
    }

    private static void Extract(string name, string dir)
    {
        using var rs = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("File " + name + " tidak ditemukan dalam installer.");
        using var fs = File.Create(Path.Combine(dir, name));
        rs.CopyTo(fs);
    }
}
