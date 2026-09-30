using System;
using System.IO;
using System.Text;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorEditor
{
    /// <summary>
    /// Load doortuning from disk: Meta XML, PSO binary (PSIN), or RSC7.
    /// </summary>
    public static class DoorTuningFileLoader
    {
        private const uint Rsc7Magic = 0x37435352; // RSC7

        public static DoorTuningDocument LoadFromPath(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (LooksLikeXml(bytes) || LooksLikeXmlPath(path))
                return DoorTuningDocument.FromXml(DecodeText(bytes));

            var ymt = LoadYmtFromDisk(path, bytes);
            if (ymt.Pso != null)
                return DoorTuningDocument.FromYmt(ymt);

            throw new InvalidDataException(
                "Not a doortuning file. Expected PSO doortuning.ymt (PSIN) or Meta XML (.xml / .ymt.pso.xml).");
        }

        private static YmtFile LoadYmtFromDisk(string path, byte[] bytes)
        {
            var name = Path.GetFileName(path);
            var ymt = new YmtFile { Name = name, FilePath = path };

            if (bytes.Length >= 4 && BitConverter.ToUInt32(bytes, 0) == Rsc7Magic)
            {
                ymt.Load(bytes);
                return ymt;
            }

            if (LooksLikePso(bytes) || IsPsoStream(bytes))
            {
                try
                {
                    var entry = CreateBinaryEntry(name, path, bytes.Length);
                    ymt.Load(bytes, entry);
                    return ymt;
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(
                        "Failed to parse PSO doortuning.ymt: " + ex.Message, ex);
                }
            }

            var text = DecodeText(bytes).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (text.StartsWith('<'))
            {
                ymt.Loaded = true;
                return ymt;
            }

            var magic = bytes.Length >= 4
                ? $"{(char)bytes[0]}{(char)bytes[1]}{(char)bytes[2]}{(char)bytes[3]}"
                : "(too short)";
            throw new InvalidDataException(
                $"Unsupported doortuning format (magic '{magic}'). Expected PSIN PSO .ymt or Meta XML.");
        }

        private static bool LooksLikeXmlPath(string path) =>
            path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ymt.xml", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".pso.xml", StringComparison.OrdinalIgnoreCase);

        private static bool LooksLikePso(byte[] bytes) =>
            bytes.Length >= 4
            && bytes[0] == (byte)'P'
            && bytes[1] == (byte)'S'
            && bytes[2] == (byte)'I'
            && bytes[3] == (byte)'N';

        private static bool IsPsoStream(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            return PsoFile.IsPSO(ms);
        }

        private static bool LooksLikeXml(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4) return false;
            int i = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                i = 3;
            while (i < bytes.Length && bytes[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                i++;
            return i < bytes.Length && bytes[i] == (byte)'<';
        }

        private static string DecodeText(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            return Encoding.UTF8.GetString(bytes);
        }

        private static RpfBinaryFileEntry CreateBinaryEntry(string name, string path, int length)
        {
            return new RpfBinaryFileEntry
            {
                Name = name,
                NameLower = name.ToLowerInvariant(),
                Path = path,
                FileSize = (uint)length,
                FileUncompressedSize = (uint)length,
            };
        }
    }
}
