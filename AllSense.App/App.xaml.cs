using System;
using System.Threading;
using System.Windows;

namespace AllSense.App;

public partial class App : Application
{
    private static Mutex? _singleInstance;

    /// <summary>Lepas mutex single-instance sebelum restart (mis. naik elevasi).</summary>
    public static void ReleaseSingleInstance()
    {
        try { _singleInstance?.ReleaseMutex(); } catch { }
    }

    public App()
    {
        InitializeComponent();

        DispatcherUnhandledException += (_, e) =>
        {
            LogCrash("dispatcher", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("appdomain", e.ExceptionObject as Exception);
    }

    internal static void LogCrash(string kind, Exception? ex)
    {
        try
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AllSense-selftest");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}\n{ex}\n");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Selftest dulu: jalankan walau ada instance GUI lain.
        var args = e?.Args;
        if (args != null)
        {
            int idx = Array.IndexOf(args, "--selftest");
            if (idx >= 0)
            {
                string? logPath = (idx + 1 < args.Length) ? args[idx + 1] : null;
                Environment.Exit(Selftest.Run(logPath));
                return;
            }
        }

        // Single-instance: jika sudah ada, keluar.
        _singleInstance = new Mutex(true, "AllSense.App.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        // Hangatkan WMI di latar SEBELUM jendela dimuat biar UI tak membeku
        // (binding {x:Static SystemInfo.*} tidak memaksa WMI di thread UI).
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            _ = SystemInfo.Manufacturer;
            _ = SystemInfo.Model;
            _ = SystemInfo.CpuName;
            _ = SystemInfo.GpuName;
            _ = SystemInfo.RamTotalLabel;
        });

        base.OnStartup(e);
    }
}
