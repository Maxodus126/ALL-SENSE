using System;
using System.IO;
using System.IO.Compression;
using AllSense.Setup;
using Xunit;

namespace AllSense.Setup.Tests;

public class SetupCoreTests
{
    [Fact]
    public void IsValidPe_RejectsSmallTextFile()
    {
        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "not an exe");
            Assert.False(SetupCore.IsValidPe(tmp));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void IsValidPe_AcceptsMZ_SizedFile()
    {
        string tmp = Path.GetTempFileName();
        try
        {
            var bytes = new byte[2 * 1024 * 1024];   // >1MB
            bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
            File.WriteAllBytes(tmp, bytes);
            Assert.True(SetupCore.IsValidPe(tmp));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void IsValidPe_RejectsSizedFile_WithoutMZ()
    {
        string tmp = Path.GetTempFileName();
        try
        {
            var bytes = new byte[2 * 1024 * 1024];   // >1MB tapi bukan MZ
            bytes[0] = (byte)'X'; bytes[1] = (byte)'Y';
            File.WriteAllBytes(tmp, bytes);
            Assert.False(SetupCore.IsValidPe(tmp));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void IsValidPe_MissingFile_ReturnsFalse()
    {
        Assert.False(SetupCore.IsValidPe(Path.Combine(Path.GetTempPath(), "tidak-ada-xyz.bin")));
    }

    [Fact]
    public void DecompressToFile_RoundTrips()
    {
        string src = Path.GetTempFileName();
        string dst = Path.GetTempFileName();
        try
        {
            byte[] payload = new byte[64 * 1024 + 13];
            new Random(42).NextBytes(payload);
            File.Delete(dst);

            using (var fs = File.Create(src))
            using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
                gz.Write(payload, 0, payload.Length);

            using (var fs = File.OpenRead(src))
                SetupCore.DecompressToFile(fs, dst);

            Assert.Equal(payload, File.ReadAllBytes(dst));
        }
        finally
        {
            File.Delete(src);
            File.Delete(dst);
        }
    }

    [Fact]
    public void DefaultInstallDir_PointsUnderLocalAppDataPrograms()
    {
        string d = SetupCore.DefaultInstallDir;
        Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), d, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("AllSense", d, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisplayVersion_IsSemver()
    {
        Assert.True(Version.TryParse(SetupCore.DisplayVersion + ".0", out _));
    }
}
