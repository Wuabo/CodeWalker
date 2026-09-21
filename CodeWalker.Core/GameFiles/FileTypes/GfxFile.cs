using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CodeWalker.GameFiles
{
    /// <summary>
    /// Helpers for GTA Scaleform .gfx &lt;-&gt; Flash .swf conversion.
    /// GTA stores uncompressed Scaleform movies with a GFX signature; tools like
    /// JPEXS expect a standard FWS/CWS/ZWS SWF header.
    /// </summary>
    public static class GfxFile
    {
        public const byte RecommendedFlashVersion = 8;

        public static bool IsSupportedMovie(byte[]? data)
        {
            return TryGetSignature(data, out _);
        }

        public static bool TryGetSignature(byte[]? data, out string signature)
        {
            signature = string.Empty;
            if (data == null || data.Length < 8) return false;
            signature = Encoding.ASCII.GetString(data, 0, 3);
            return signature is "GFX" or "FWS" or "CWS" or "ZWS";
        }

        public static string GetSwfFileName(string gfxName)
        {
            if (string.IsNullOrEmpty(gfxName)) return "movie.swf";
            if (gfxName.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase))
                return gfxName.Substring(0, gfxName.Length - 4) + ".swf";
            return Path.ChangeExtension(gfxName, ".swf") ?? (gfxName + ".swf");
        }

        public static string GetGfxFileName(string swfName)
        {
            if (string.IsNullOrEmpty(swfName)) return "movie.gfx";
            if (swfName.EndsWith(".swf", StringComparison.OrdinalIgnoreCase) ||
                swfName.EndsWith(".fws", StringComparison.OrdinalIgnoreCase) ||
                swfName.EndsWith(".cws", StringComparison.OrdinalIgnoreCase) ||
                swfName.EndsWith(".zws", StringComparison.OrdinalIgnoreCase))
                return swfName.Substring(0, swfName.Length - 4) + ".gfx";
            if (swfName.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase))
                return swfName;
            return Path.ChangeExtension(swfName, ".gfx") ?? (swfName + ".gfx");
        }

        /// <summary>
        /// Convert a Scaleform GFX (or already-SWF) movie to uncompressed FWS SWF for editing.
        /// </summary>
        public static byte[] ToSwf(byte[] data, out List<string> warnings)
        {
            warnings = new List<string>();
            var uncompressed = EnsureUncompressed(data, "FWS", warnings);
            AddCompatibilityWarnings(uncompressed, warnings, forImport: false);
            return uncompressed;
        }

        /// <summary>
        /// Convert an FWS/CWS/ZWS/GFX movie to uncompressed Scaleform GFX for GTA/FiveM.
        /// CWS is decompressed; ZWS (LZMA) is not supported.
        /// </summary>
        public static byte[] ToGfx(byte[] data, out List<string> warnings)
        {
            warnings = new List<string>();
            var uncompressed = EnsureUncompressed(data, "GFX", warnings);
            AddCompatibilityWarnings(uncompressed, warnings, forImport: true);
            return uncompressed;
        }

        private static byte[] EnsureUncompressed(byte[] data, string targetSignature, List<string> warnings)
        {
            if (!TryGetSignature(data, out var signature))
                throw new InvalidDataException("Not a Scaleform/Flash movie (expected GFX/FWS/CWS/ZWS header).");

            byte[] body;
            switch (signature)
            {
                case "GFX":
                case "FWS":
                    body = (byte[])data.Clone();
                    break;
                case "CWS":
                    body = DecompressCws(data);
                    warnings.Add("Decompressed zlib-compressed SWF (CWS) to uncompressed movie.");
                    break;
                case "ZWS":
                    throw new NotSupportedException(
                        "LZMA-compressed SWF (ZWS) is not supported. Open it in JPEXS Free Flash Decompiler and save as uncompressed SWF (FWS), then import again.");
                default:
                    throw new InvalidDataException("Unsupported movie signature: " + signature);
            }

            body[0] = (byte)targetSignature[0];
            body[1] = (byte)targetSignature[1];
            body[2] = (byte)targetSignature[2];

            uint declared = BitConverter.ToUInt32(body, 4);
            if (declared != (uint)body.Length)
            {
                BitConverter.TryWriteBytes(body.AsSpan(4, 4), (uint)body.Length);
                warnings.Add($"Updated SWF length field from {declared} to {body.Length}.");
            }

            return body;
        }

        private static byte[] DecompressCws(byte[] data)
        {
            uint uncompressedLength = BitConverter.ToUInt32(data, 4);
            if (uncompressedLength < 8 || uncompressedLength > 512u * 1024u * 1024u)
                throw new InvalidDataException("Invalid CWS uncompressed length: " + uncompressedLength);

            var result = new byte[uncompressedLength];
            result[0] = (byte)'F';
            result[1] = (byte)'W';
            result[2] = (byte)'S';
            result[3] = data[3];
            BitConverter.TryWriteBytes(result.AsSpan(4, 4), uncompressedLength);

            using var input = new MemoryStream(data, 8, data.Length - 8);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            int offset = 8;
            while (offset < result.Length)
            {
                int read = zlib.Read(result, offset, result.Length - offset);
                if (read <= 0) break;
                offset += read;
            }

            if (offset != result.Length)
                throw new InvalidDataException($"CWS decompress produced {offset} bytes, expected {result.Length}.");

            return result;
        }

        private static void AddCompatibilityWarnings(byte[] data, List<string> warnings, bool forImport)
        {
            byte version = data[3];
            if (version > RecommendedFlashVersion)
            {
                warnings.Add(
                    $"Flash version is {version}. GTA Scaleform typically targets Flash Player {RecommendedFlashVersion} / ActionScript 2. Higher versions (especially AS3) may not run in-game.");
            }

            if (ContainsAs3Tags(data))
            {
                warnings.Add(
                    "ActionScript 3 tags (DoABC/SymbolClass) were detected. GTA's Scaleform GFx runtime expects ActionScript 2 and will likely ignore or fail this movie.");
            }

            warnings.Add(
                forImport
                    ? "Textures are not embedded: keep the matching .ytd (same base name) next to this .gfx if the movie uses external images."
                    : "Textures are not embedded in the SWF: export/edit the matching .ytd separately if the movie uses external images (img:// / txd).");
        }

        private static bool ContainsAs3Tags(byte[] data)
        {
            try
            {
                int pos = 8;
                int nBits = data[pos] >> 3;
                int rectBits = 5 + nBits * 4;
                pos += (rectBits + 7) / 8;
                if (pos + 4 > data.Length) return false;
                pos += 4; // FrameRate + FrameCount

                while (pos + 2 <= data.Length)
                {
                    ushort codeAndLength = BitConverter.ToUInt16(data, pos);
                    pos += 2;
                    int tagCode = codeAndLength >> 6;
                    int length = codeAndLength & 0x3F;
                    if (length == 0x3F)
                    {
                        if (pos + 4 > data.Length) break;
                        length = BitConverter.ToInt32(data, pos);
                        pos += 4;
                    }
                    if (length < 0 || pos + length > data.Length) break;

                    // 82 = DoABC, 72 was DoAction (AS1/2), 76 = SymbolClass (AS3 linkage)
                    if (tagCode is 82 or 76) return true;
                    if (tagCode == 0) break; // End
                    pos += length;
                }
            }
            catch
            {
                // Tag scan is best-effort; header conversion still succeeded.
            }
            return false;
        }
    }
}