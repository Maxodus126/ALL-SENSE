using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AllSense.App;

/// <summary>Versi aplikasi — sumber tunggal dari assembly.</summary>
public static class AppInfo
{
    public static Version AssemblyVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
    public static string VersionString => AssemblyVersion.ToString(3);   // "1.2.0"
}

/// <summary>Periksa & pasang update dari GitHub Releases (stable, tanpa prerelease).</summary>
public static class AppUpdate
{
    /// <summary>Repo GitHub bawaan — dipakai otomatis kalau Settings kosong.</summary>
    public const string DefaultRepo = "Maxodus126/ALL-SENSE";

    // Short timeout buat cek metadata; long timeout buat unduh (zip besar).
    private static readonly HttpClient Http = CreateHttp(TimeSpan.FromSeconds(20));
    private static readonly HttpClient HttpLong = CreateHttp(TimeSpan.FromMinutes(10));

    private static HttpClient CreateHttp(TimeSpan timeout)
    {
        var h = new HttpClient { Timeout = timeout };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("AllSense-updater/1.0");
        return h;
    }

    private static HttpRequestMessage Req(string url, string? token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    public sealed class Result
    {
        public bool Ok;
        public string? Error;      // diisi bila gagal (jaringan/repo/tak ada rilis)
        public Version? Latest;    // versi rilis terbaru
        public string? Tag;
        public string? Notes;
        public string? AssetUrl;
        public string? AssetName;
    }

    /// <summary>Ambil rilis "latest" dari GitHub, pilih asset .zip (prefer x64). Token opsional (repo privat / rate-limit).</summary>
    public static async Task<Result> CheckAsync(string repo, string? token = null)
    {
        var r = new Result();
        try
        {
            if (string.IsNullOrWhiteSpace(repo)) repo = DefaultRepo;
            if (!IsValidRepo(repo))
            { r.Error = "Repo belum diset dengan benar (format: owner/repo)."; return r; }

            using var req = Req($"https://api.github.com/repos/{repo.Trim()}/releases/latest", token);
            using var resp = await Http.SendAsync(req);
            if ((int)resp.StatusCode == 404)
            { r.Error = $"Tidak ada rilis 'latest' untuk {repo.Trim()}."; return r; }
            if ((int)resp.StatusCode is 403 or 429)
            { r.Error = "Terlalu banyak permintaan ke GitHub (rate-limit). Coba lagi nanti."; return r; }
            if (!resp.IsSuccessStatusCode)
            { r.Error = $"GitHub membalas HTTP {(int)resp.StatusCode}."; return r; }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            if (!TryParseTag(tag, out var v))
            { r.Error = "Tag rilis tidak dikenali (" + tag + ")."; return r; }

            r.Tag = tag;
            r.Latest = v;
            r.Notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    // Prefer asset bertanda x64; kalau tidak ada, pakai zip pertama.
                    if (r.AssetName == null || name.Contains("x64", StringComparison.OrdinalIgnoreCase))
                    {
                        r.AssetName = name;
                        r.AssetUrl = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    }
                }
            }

            if (r.AssetUrl == null) { r.Error = "Asset .zip tidak ditemukan di rilis."; return r; }
            r.Ok = true;
            return r;
        }
        catch (Exception ex) { r.Error = "Gagal cek update: " + ex.Message; return r; }
    }

    private static readonly Regex RepoRx = new(@"^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    private static bool IsValidRepo(string repo)
        => RepoRx.IsMatch(repo.Trim());

    /// <summary>Parse tag semver: "v1.2.3", "1.2.3", abaikan suffix prerelease (-beta dst).</summary>
    internal static bool TryParseTag(string tag, out Version v)
    {
        v = new Version(0, 0, 0);
        string s = tag.Trim().TrimStart('v', 'V');
        int dash = s.IndexOf('-');
        if (dash >= 0) s = s[..dash];
        var parts = s.Split('.').Take(4).Select(p => int.TryParse(p, out var x) ? x : 0).ToArray();
        if (parts.Length < 3) return false;
        v = parts.Length == 3
            ? new Version(parts[0], parts[1], parts[2])
            : new Version(parts[0], parts[1], parts[2], parts[3]);
        return true;
    }

    /// <summary>Unduh asset rilis ke <paramref name="destDir"/>. Kembali path zip. Token opsional.</summary>
    public static async Task<string> DownloadAsync(string url, string destDir, string? token = null)
    {
        Directory.CreateDirectory(destDir);
        string dest = Path.Combine(destDir, "AllSense-new.zip");
        using (var req = Req(url, token))
        using (var resp = await HttpLong.SendAsync(req))
        {
            resp.EnsureSuccessStatusCode();
            using var fs = File.Create(dest);
            await resp.Content.CopyToAsync(fs);
        }
        return dest;
    }

    /// <summary>
    /// Pasang update: ekstrak zip ke temp, tulis updater.bat yang menunggu proses
    /// AllSense keluar, lalu menyalin exe baru menimpa exe lama dan restart.
    /// Wajib dipanggil sebelum aplikasi ditutup (updater menunggu PID ini mati).
    /// </summary>
    public static void Install(string zipPath, string exePath)
    {
        // Guard: hanya izinkan ganti biner AllSense itu sendiri. Kalau app dijalankan
        // via 'dotnet AllSense.App.dll', ProcessPath menunjuk dotnet.exe — menimpa
        // itu bakal merusak sistem. Tolak.
        if (!Path.GetFileName(exePath).Equals("AllSense.App.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Update hanya diizinkan dari biner AllSense.App.exe (jalan kan app dari exe standalone, bukan dotnet dll).");

        string dir = Path.Combine(Path.GetTempPath(), "AllSense-update");
        string extract = Path.Combine(dir, "new");
        if (Directory.Exists(extract)) Directory.Delete(extract, true);
        ZipFile.ExtractToDirectory(zipPath, extract);

        string newExe = Directory.EnumerateFiles(extract, "*.exe", SearchOption.AllDirectories).FirstOrDefault()
                        ?? Path.Combine(extract, Path.GetFileName(exePath));
        if (!File.Exists(newExe)) throw new FileNotFoundException("Exe update tidak ditemukan setelah ekstrak.");

        int pid = Environment.ProcessId;
        string bat = Path.Combine(dir, "updater.bat");
        File.WriteAllText(bat,
            "@echo off\r\n" +
            "set \"PID=" + pid + "\"\r\n" +
            "set \"NEW=" + newExe + "\"\r\n" +
            "set \"OLD=" + exePath + "\"\r\n" +
            ":wait\r\n" +
            "tasklist /FI \"PID eq %PID%\" 2>nul | findstr /B /C:\"%PID%\" >nul && ( timeout /t 1 /nobreak >nul & goto wait )\r\n" +
            "copy /Y \"%NEW%\" \"%OLD%\" >nul\r\n" +
            "if errorlevel 1 (\r\n" +
            "  echo FAILED_COPY > \"%TEMP%\\AllSense-update\\apply.log\"\r\n" +
            "  start \"\" \"%OLD%\"\r\n" +   // gagal timpa: relaunch versi lama biar tak terdampar
            "  del \"%~f0\"\r\n" +
            "  exit /b 1\r\n" +
            ")\r\n" +
            "echo OK > \"%TEMP%\\AllSense-update\\apply.log\"\r\n" +
            "start \"\" \"%OLD%\"\r\n" +
            "del \"%~f0\"\r\n");

        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = bat,
            UseShellExecute = true,
            WorkingDirectory = dir,
        });
        if (p is null) throw new InvalidOperationException("Gagal menjalankan updater.");
    }
}
