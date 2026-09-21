using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CodeWalker.GameFiles
{
    /// <summary>
    /// Helpers for GTA Scaleform .gfx &lt;-&gt; Flash .swf conversion and lightweight inspection.
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

        public static string GetCompanionYtdFileName(string gfxName)
        {
            if (string.IsNullOrEmpty(gfxName)) return "movie.ytd";
            if (gfxName.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase) ||
                gfxName.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
                return gfxName.Substring(0, gfxName.Length - 4) + ".ytd";
            return Path.ChangeExtension(gfxName, ".ytd") ?? (gfxName + ".ytd");
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
        /// </summary>
        public static byte[] ToGfx(byte[] data, out List<string> warnings)
        {
            warnings = new List<string>();
            var uncompressed = EnsureUncompressed(data, "GFX", warnings);
            AddCompatibilityWarnings(uncompressed, warnings, forImport: true);
            return uncompressed;
        }

        /// <summary>
        /// Best-effort structural inspection for the Gfx viewer (not a full SWF parser).
        /// </summary>
        public static GfxMovieInfo Inspect(byte[] data)
        {
            if (!TryGetSignature(data, out var signature))
                throw new InvalidDataException("Not a Scaleform/Flash movie (expected GFX/FWS/CWS/ZWS header).");

            var warnings = new List<string>();
            byte[] body = data;
            if (signature == "CWS")
            {
                body = DecompressCws(data);
                warnings.Add("Source was zlib-compressed (CWS); inspected decompressed movie.");
            }
            else if (signature == "ZWS")
            {
                throw new NotSupportedException("LZMA-compressed SWF (ZWS) cannot be inspected here.");
            }

            var info = new GfxMovieInfo
            {
                Signature = Encoding.ASCII.GetString(body, 0, 3),
                Version = body[3],
                FileLength = BitConverter.ToUInt32(body, 4),
                ActualLength = body.Length,
                Warnings = warnings
            };

            int pos = 8;
            if (pos >= body.Length) return info;

            int nBits = body[pos] >> 3;
            int rectBits = 5 + nBits * 4;
            int rectBytes = (rectBits + 7) / 8;
            info.FrameSizeBits = nBits;
            pos += rectBytes;
            if (pos + 4 > body.Length) return info;

            // FrameRate is stored as 8.8 fixed
            ushort frameRateRaw = BitConverter.ToUInt16(body, pos);
            info.FrameRate = frameRateRaw / 256f;
            pos += 2;
            info.FrameCount = BitConverter.ToUInt16(body, pos);
            pos += 2;

            var tagCounts = new Dictionary<int, int>();
            var exports = new List<GfxExportSymbol>();
            var imports = new List<string>();
            var imgRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int spriteCount = 0;
            int editTextCount = 0;
            int shapeCount = 0;
            int placeCount = 0;
            int externalImageCount = 0;

            while (pos + 2 <= body.Length)
            {
                ushort codeAndLength = BitConverter.ToUInt16(body, pos);
                pos += 2;
                int tagCode = codeAndLength >> 6;
                int length = codeAndLength & 0x3F;
                if (length == 0x3F)
                {
                    if (pos + 4 > body.Length) break;
                    length = BitConverter.ToInt32(body, pos);
                    pos += 4;
                }
                if (length < 0 || pos + length > body.Length) break;

                tagCounts.TryGetValue(tagCode, out int c);
                tagCounts[tagCode] = c + 1;

                int payload = pos;
                switch (tagCode)
                {
                    case 2:
                    case 22:
                    case 32:
                    case 83:
                        shapeCount++;
                        break;
                    case 4:
                    case 26:
                    case 70:
                        placeCount++;
                        break;
                    case 37:
                        editTextCount++;
                        break;
                    case 39:
                        spriteCount++;
                        break;
                    case 56: // ExportAssets
                        ReadExportAssets(body, payload, length, exports);
                        break;
                    case 57: // ImportAssets
                    case 71: // ImportAssets2
                        ReadImportAssets(body, payload, length, imports);
                        break;
                    case 1000:
                    case 1001:
                    case 1008:
                    case 1009:
                        externalImageCount++;
                        break;
                    case 0:
                        pos += length;
                        goto done;
                }

                pos += length;
            }

        done:
            info.TagCounts = tagCounts;
            info.Exports = exports;
            info.Imports = imports;
            info.ShapeCount = shapeCount;
            info.SpriteCount = spriteCount;
            info.EditTextCount = editTextCount;
            info.PlaceObjectCount = placeCount;
            info.ExternalImageTagCount = externalImageCount;

            CollectAsciiRefs(body, imgRefs);
            info.ImageRefs = new List<string>(imgRefs);
            info.ImageRefs.Sort(StringComparer.OrdinalIgnoreCase);

            if (info.Version > RecommendedFlashVersion)
                info.Warnings.Add($"Flash version {info.Version} is higher than typical GTA Scaleform target ({RecommendedFlashVersion}).");
            if (ContainsAs3Tags(body))
                info.Warnings.Add("ActionScript 3 tags detected; GTA Scaleform expects ActionScript 2.");
            if (placeCount == 0 && spriteCount > 0)
                info.Warnings.Add("No PlaceObject tags on the root timeline — layout is likely built at runtime by ActionScript + TXD textures.");

            return info;
        }

        private static void ReadExportAssets(byte[] data, int payload, int length, List<GfxExportSymbol> exports)
        {
            if (length < 2) return;
            int end = payload + length;
            int p = payload;
            int count = BitConverter.ToUInt16(data, p);
            p += 2;
            for (int i = 0; i < count && p + 3 <= end; i++)
            {
                ushort id = BitConverter.ToUInt16(data, p);
                p += 2;
                string name = ReadNullString(data, ref p, end);
                if (!string.IsNullOrEmpty(name))
                    exports.Add(new GfxExportSymbol { CharacterId = id, Name = name });
            }
        }

        private static void ReadImportAssets(byte[] data, int payload, int length, List<string> imports)
        {
            if (length < 1) return;
            int end = payload + length;
            int p = payload;
            // ImportAssets2 starts with url string
            string url = ReadNullString(data, ref p, end);
            if (!string.IsNullOrEmpty(url))
                imports.Add(url);
            if (p + 2 > end) return;
            // skip count / pairs best-effort; capture additional names
            while (p < end)
            {
                // optional UI16 id then name, or just names depending on tag — collect printable strings
                if (p + 2 <= end && data[p] == 0) { p++; continue; }
                if (p + 2 <= end && data[p + 1] == 0 && data[p] >= 32)
                {
                    // likely start of string without id
                }
                else if (p + 3 <= end)
                {
                    // try id + name
                    p += 2;
                }
                string name = ReadNullString(data, ref p, end);
                if (!string.IsNullOrEmpty(name) && name.Length > 1)
                    imports.Add(name);
                else
                    break;
            }
        }

        private static string ReadNullString(byte[] data, ref int p, int end)
        {
            var sb = new StringBuilder();
            while (p < end)
            {
                byte b = data[p++];
                if (b == 0) break;
                if (b >= 32 && b <= 126) sb.Append((char)b);
                else { sb.Clear(); break; }
            }
            return sb.ToString();
        }

        private static void CollectAsciiRefs(byte[] data, HashSet<string> imgRefs)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                if (b >= 32 && b <= 126) sb.Append((char)b);
                else
                {
                    if (sb.Length >= 6) ConsiderString(sb.ToString(), imgRefs);
                    sb.Clear();
                }
            }
            if (sb.Length >= 6) ConsiderString(sb.ToString(), imgRefs);
        }

        private static void ConsiderString(string s, HashSet<string> imgRefs)
        {
            if (s.StartsWith("img://", StringComparison.OrdinalIgnoreCase))
            {
                imgRefs.Add(s);
                return;
            }
            // Rockstar often concatenates dict/texture in AS; capture bare img:// fragments already handled.
            int idx = s.IndexOf("img://", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                imgRefs.Add(s.Substring(idx));
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
                pos += 4;

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
                    if (tagCode is 82 or 76) return true;
                    if (tagCode == 0) break;
                    pos += length;
                }
            }
            catch
            {
            }
            return false;
        }
    }

    public class GfxMovieInfo
    {
        public string Signature { get; set; } = "";
        public byte Version { get; set; }
        public uint FileLength { get; set; }
        public int ActualLength { get; set; }
        public float FrameRate { get; set; }
        public ushort FrameCount { get; set; }
        public int FrameSizeBits { get; set; }
        public int ShapeCount { get; set; }
        public int SpriteCount { get; set; }
        public int EditTextCount { get; set; }
        public int PlaceObjectCount { get; set; }
        public int ExternalImageTagCount { get; set; }
        public Dictionary<int, int> TagCounts { get; set; } = new();
        public List<GfxExportSymbol> Exports { get; set; } = new();
        public List<string> Imports { get; set; } = new();
        public List<string> ImageRefs { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class GfxExportSymbol
    {
        public ushort CharacterId { get; set; }
        public string Name { get; set; } = "";
        public override string ToString() => $"{CharacterId}: {Name}";
    }
}
