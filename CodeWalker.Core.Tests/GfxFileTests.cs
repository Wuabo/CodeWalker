using System.IO.Compression;
using System.Text;
using CodeWalker.GameFiles;
using Xunit;

namespace CodeWalker.Core.Tests;

public class GfxFileTests
{
    [Fact]
    public void RoundTripGfxToSwfPreservesBodyAndFixesHeader()
    {
        var gfx = BuildUncompressedMovie("GFX", 8, [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]);

        var swf = GfxFile.ToSwf(gfx, out var exportWarnings);
        Assert.Equal((byte)'F', swf[0]);
        Assert.Equal((byte)'W', swf[1]);
        Assert.Equal((byte)'S', swf[2]);
        Assert.Equal(gfx.AsSpan(3).ToArray(), swf.AsSpan(3).ToArray());
        Assert.Contains(exportWarnings, w => w.Contains(".ytd", StringComparison.OrdinalIgnoreCase));

        var roundTrip = GfxFile.ToGfx(swf, out var importWarnings);
        Assert.Equal(gfx, roundTrip);
        Assert.Contains(importWarnings, w => w.Contains(".ytd", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ToGfxDecompressesCwsAndUpdatesLength()
    {
        var uncompressedBody = new byte[32];
        // Minimal-ish RECT / frame header bytes so tag scan does not throw.
        uncompressedBody[0] = 0x78; // nBits-ish placeholder
        var fws = BuildUncompressedMovie("FWS", 8, uncompressedBody);
        // Corrupt length to ensure fixer runs after decompress path via CWS
        var cws = CompressToCws(fws);
        // Force wrong declared length in CWS header to still decompress using original length field
        // (CompressToCws writes correct length; verify output is GFX)
        var gfx = GfxFile.ToGfx(cws, out var warnings);
        Assert.Equal((byte)'G', gfx[0]);
        Assert.Equal((byte)'F', gfx[1]);
        Assert.Equal((byte)'X', gfx[2]);
        Assert.Equal(fws.Length, gfx.Length);
        Assert.Equal(fws.AsSpan(3).ToArray(), gfx.AsSpan(3).ToArray());
        Assert.Contains(warnings, w => w.Contains("CWS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ToGfxRejectsZws()
    {
        var zws = Encoding.ASCII.GetBytes("ZWS");
        var data = new byte[16];
        Array.Copy(zws, data, 3);
        data[3] = 13;
        BitConverter.TryWriteBytes(data.AsSpan(4, 4), 16u);
        Assert.Throws<NotSupportedException>(() => GfxFile.ToGfx(data, out _));
    }

    [Fact]
    public void FileNameHelpers()
    {
        Assert.Equal("pause_menu_header.swf", GfxFile.GetSwfFileName("pause_menu_header.gfx"));
        Assert.Equal("pause_menu_header.gfx", GfxFile.GetGfxFileName("pause_menu_header.swf"));
        Assert.Equal("pause_menu_header.gfx", GfxFile.GetGfxFileName("pause_menu_header.fws"));
        Assert.Equal("pause_menu_header.gfx", GfxFile.GetGfxFileName("pause_menu_header.gfx"));
    }

    [Fact]
    public void ExportImportRoundTripWritesDisk()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "cw_gfx_roundtrip_test");
        Directory.CreateDirectory(outDir);

        var name = "sample_movie.gfx";
        var gfx = BuildUncompressedMovie("GFX", 8, [0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80]);

        var swf = GfxFile.ToSwf(gfx, out var exportWarnings);
        Assert.True(GfxFile.IsSupportedMovie(swf));
        Assert.Equal("FWS", Encoding.ASCII.GetString(swf, 0, 3));
        Assert.Contains(exportWarnings, w => w.Contains(".ytd", StringComparison.OrdinalIgnoreCase));

        var swfPath = Path.Combine(outDir, GfxFile.GetSwfFileName(name));
        File.WriteAllBytes(swfPath, swf);

        var swfFromDisk = File.ReadAllBytes(swfPath);
        var gfxBack = GfxFile.ToGfx(swfFromDisk, out var importWarnings);
        Assert.Equal("GFX", Encoding.ASCII.GetString(gfxBack, 0, 3));
        Assert.Equal((uint)gfxBack.Length, BitConverter.ToUInt32(gfxBack, 4));
        Assert.Equal(gfx, gfxBack);
        Assert.Contains(importWarnings, w => w.Contains(".ytd", StringComparison.OrdinalIgnoreCase));

        var gfxOutPath = Path.Combine(outDir, GfxFile.GetGfxFileName(Path.GetFileName(swfPath)));
        File.WriteAllBytes(gfxOutPath, gfxBack);
        Assert.Equal(gfx, File.ReadAllBytes(gfxOutPath));
    }

    private static byte[] BuildUncompressedMovie(string signature, byte version, byte[] bodyAfterHeader)
    {
        var data = new byte[8 + bodyAfterHeader.Length];
        data[0] = (byte)signature[0];
        data[1] = (byte)signature[1];
        data[2] = (byte)signature[2];
        data[3] = version;
        BitConverter.TryWriteBytes(data.AsSpan(4, 4), (uint)data.Length);
        Array.Copy(bodyAfterHeader, 0, data, 8, bodyAfterHeader.Length);
        return data;
    }

    private static byte[] CompressToCws(byte[] fws)
    {
        using var ms = new MemoryStream();
        ms.WriteByte((byte)'C');
        ms.WriteByte((byte)'W');
        ms.WriteByte((byte)'S');
        ms.WriteByte(fws[3]);
        ms.Write(fws, 4, 4); // uncompressed length
        using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(fws, 8, fws.Length - 8);
        }
        return ms.ToArray();
    }
}