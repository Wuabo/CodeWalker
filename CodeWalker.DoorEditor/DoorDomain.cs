using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorEditor
{
    public static class DoorConstants
    {
        public static readonly (string Id, string Label)[] DoorTypes =
        [
            ("7", "Normal Door"),
            ("5", "Garage Door"),
            ("8", "Sliding Door"),
            ("10", "Sliding Vertical Door"),
            ("9", "Barrier Door"),
            ("12", "Rail Crossing Barrier Door"),
        ];

        public const int FlagsNormal = 67_239_936;
        public const int FlagsAutomatic = 604_110_848;

        public static int FlagsForType(string specialAttribute) =>
            specialAttribute is "5" or "8" or "9" or "10" or "12" ? FlagsAutomatic : FlagsNormal;

        public static bool IsFlagsPreset(int flags) =>
            flags == FlagsNormal || flags == FlagsAutomatic;

        public static string? FlagsPresetLabel(int flags) =>
            flags == FlagsNormal ? "Normal" : flags == FlagsAutomatic ? "Automatic" : null;
    }

    /// <summary>
    /// Load doortuning from disk: Meta XML (incl. FiveM .ymt), PSO binary, or RSC7.
    /// </summary>
    public static class DoorTuningFileLoader
    {
        private const uint Rsc7Magic = 0x37435352; // RSC7

        public static DoorTuningDocument LoadFromPath(string path)
        {
            var bytes = File.ReadAllBytes(path);
            // Prefer content sniff over extension — .ymt may be Meta XML or PSO binary;
            // .ymt.pso.xml is always Meta XML from CodeWalker.
            if (LooksLikeXml(bytes) || LooksLikeXmlPath(path))
                return DoorTuningDocument.FromXml(DecodeText(bytes));

            var ymt = LoadYmtFromDisk(path, bytes);
            if (ymt.Pso != null)
                return DoorTuningDocument.FromYmt(ymt);

            throw new InvalidDataException(
                "Not a doortuning file. Expected PSO doortuning.ymt (PSIN) or Meta XML (.xml / .ymt.pso.xml).");
        }

        public static YmtFile LoadYmtFromDisk(string path) =>
            LoadYmtFromDisk(path, File.ReadAllBytes(path));

        private static YmtFile LoadYmtFromDisk(string path, byte[] bytes)
        {
            var name = Path.GetFileName(path);
            var ymt = new YmtFile { Name = name, FilePath = path };

            // FiveM resources often ship Meta XML content inside doortuning.ymt —
            // that path is handled by LoadFromPath via FromXml; here we only load PSO.
            if (LooksLikeXml(bytes) || LooksLikeXmlPath(path))
            {
                ymt.Loaded = true;
                return ymt;
            }

            if (bytes.Length >= 4 && BitConverter.ToUInt32(bytes, 0) == Rsc7Magic)
            {
                ymt.Load(bytes);
                return ymt;
            }

            // Vanilla doortuning.ymt is raw PSO (PSIN…). Detect by magic bytes first —
            // more reliable than stream helpers when callers left Position ≠ 0.
            if (LooksLikePso(bytes) || IsPsoStream(bytes))
            {
                try
                {
                    var entry = CreateBinaryEntry(name, path, bytes.Length);
                    ymt.Load(bytes, entry);
                    if (ymt.Pso != null)
                        return ymt;
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(
                        "Failed to parse PSO doortuning.ymt: " + ex.Message, ex);
                }
            }

            // Last chance: text that failed the quick XML sniff (encoding quirks)
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
            // UTF-8 BOM
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                i = 3;
            while (i < bytes.Length && (bytes[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
                i++;
            if (i >= bytes.Length) return false;
            // '<' or "<?xml"
            return bytes[i] == (byte)'<';
        }

        private static string DecodeText(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        private static RpfBinaryFileEntry CreateBinaryEntry(string name, string path, int length)
        {
            var lower = name.ToLowerInvariant();
            return new RpfBinaryFileEntry
            {
                Name = name,
                NameLower = lower,
                NameHash = JenkHash.GenHash(lower),
                ShortNameHash = JenkHash.GenHash(Path.GetFileNameWithoutExtension(lower)),
                Path = path,
                FileSize = (uint)length,
                FileUncompressedSize = (uint)length
            };
        }
    }

    public sealed class YtypArchetypeItem
    {
        public string Name { get; set; } = "";
        public string SpecialAttribute { get; set; } = "0";
        public string Flags { get; set; } = "0";
        public bool UseFlags { get; set; }
    }

    public static class YtypXmlEditor
    {
        public static List<YtypArchetypeItem> Parse(string xml)
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root ?? throw new InvalidOperationException("Empty YTYP XML.");
            var list = new List<YtypArchetypeItem>();
            foreach (var item in root.Descendants("Item"))
            {
                var sa = item.Element("specialAttribute");
                if (sa == null) continue;
                var name = item.Element("name")?.Value
                    ?? item.Element("assetName")?.Value
                    ?? "";
                var flagsEl = item.Element("flags");
                var flags = flagsEl?.Attribute("value")?.Value ?? flagsEl?.Value ?? "0";
                var flagsNum = int.TryParse(flags, out var f) ? f : 0;
                list.Add(new YtypArchetypeItem
                {
                    Name = name,
                    SpecialAttribute = sa.Attribute("value")?.Value ?? sa.Value ?? "0",
                    Flags = flags,
                    UseFlags = DoorConstants.IsFlagsPreset(flagsNum)
                });
            }
            return list;
        }

        public static string UpdateSpecialAttribute(string xml, string name, string value)
        {
            return ReplaceAttrInItem(xml, name, "specialAttribute", value);
        }

        public static string UpdateFlags(string xml, string name, string value)
        {
            if (Regex.IsMatch(xml, $@"(<name>\s*{Regex.Escape(name)}\s*</name>[\s\S]*?<flags\b[^>]*value="")[^""]*("")", RegexOptions.IgnoreCase))
            {
                return Regex.Replace(xml,
                    $@"(<name>\s*{Regex.Escape(name)}\s*</name>[\s\S]*?<flags\b[^>]*value="")[^""]*("")",
                    m => m.Groups[1].Value + value + m.Groups[2].Value,
                    RegexOptions.IgnoreCase);
            }

            // insert flags after specialAttribute
            return Regex.Replace(xml,
                $@"(<name>\s*{Regex.Escape(name)}\s*</name>[\s\S]*?<specialAttribute\b[^>]*/>)",
                m => m.Groups[1].Value + $"\n   <flags value=\"{value}\" />",
                RegexOptions.IgnoreCase);
        }

        private static string ReplaceAttrInItem(string xml, string name, string attrName, string value)
        {
            return Regex.Replace(xml,
                $@"(<name>\s*{Regex.Escape(name)}\s*</name>[\s\S]*?<{attrName}\b[^>]*value="")[^""]*("")",
                m => m.Groups[1].Value + value + m.Groups[2].Value,
                RegexOptions.IgnoreCase);
        }
    }

    public static class NametableUtil
    {
        public static string Encode(IEnumerable<string> names)
        {
            var outSet = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var raw in names.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                var n = raw.Trim();
                outSet.Add(n);
                if (n.StartsWith("d_", StringComparison.OrdinalIgnoreCase) && n.Length > 2)
                {
                    // dasl_ + jenkins of model part — keep simple: also store without prefix handled by audio side
                    outSet.Add(n);
                }
            }
            return outSet.Count == 0 ? "" : string.Join("\0", outSet) + "\0";
        }

        public static List<string> Parse(string text) =>
            text.Split(['\0', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    public sealed class MergeConflict
    {
        public string Kind { get; init; } = "";
        public string Key { get; init; } = "";
        public string Existing { get; init; } = "";
        public string Incoming { get; init; } = "";
        public string? Source { get; init; }
    }

    public sealed class MergeResult
    {
        public DoorTuningDocument Document { get; set; } = new();
        public List<string> AddTunings { get; } = new();
        public List<(string Model, string Tuning)> AddMaps { get; } = new();
        public List<MergeConflict> Conflicts { get; } = new();
    }

    public static class DoorTuningMerge
    {
        public static MergeResult Merge(DoorTuningDocument main, DoorTuningDocument incoming, string? sourceName = null)
        {
            var result = new MergeResult
            {
                Document = new DoorTuningDocument
                {
                    NamedTunings = main.NamedTunings.Select(CloneTuning).ToList(),
                    ModelMappings = main.ModelMappings.Select(m => new DoorModelMapping
                    {
                        ModelName = m.ModelName,
                        TuningName = m.TuningName
                    }).ToList()
                }
            };

            var knownT = new HashSet<string>(result.Document.NamedTunings.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
            var knownM = result.Document.ModelMappings.ToDictionary(m => m.ModelName, m => m.TuningName, StringComparer.OrdinalIgnoreCase);

            foreach (var t in incoming.NamedTunings)
            {
                if (!knownT.Contains(t.Name))
                {
                    result.Document.NamedTunings.Add(CloneTuning(t));
                    knownT.Add(t.Name);
                    result.AddTunings.Add(t.Name);
                }
                else
                {
                    result.Conflicts.Add(new MergeConflict
                    {
                        Kind = "tuning",
                        Key = t.Name,
                        Existing = "kept",
                        Incoming = "differs",
                        Source = sourceName
                    });
                }
            }

            foreach (var m in incoming.ModelMappings)
            {
                if (!knownM.ContainsKey(m.ModelName))
                {
                    result.Document.ModelMappings.Add(new DoorModelMapping
                    {
                        ModelName = m.ModelName,
                        TuningName = m.TuningName
                    });
                    knownM[m.ModelName] = m.TuningName;
                    result.AddMaps.Add((m.ModelName, m.TuningName));
                }
                else if (!string.Equals(knownM[m.ModelName], m.TuningName, StringComparison.OrdinalIgnoreCase))
                {
                    result.Conflicts.Add(new MergeConflict
                    {
                        Kind = "map",
                        Key = m.ModelName,
                        Existing = knownM[m.ModelName],
                        Incoming = m.TuningName,
                        Source = sourceName
                    });
                }
            }

            return result;
        }

        public static MergeResult MergeMany(DoorTuningDocument main, IEnumerable<(string Name, DoorTuningDocument Doc)> incoming)
        {
            var acc = new MergeResult
            {
                Document = new DoorTuningDocument
                {
                    NamedTunings = main.NamedTunings.Select(CloneTuning).ToList(),
                    ModelMappings = main.ModelMappings.Select(m => new DoorModelMapping
                    {
                        ModelName = m.ModelName,
                        TuningName = m.TuningName
                    }).ToList()
                }
            };

            foreach (var (name, doc) in incoming)
            {
                var step = Merge(acc.Document, doc, name);
                acc.Document = step.Document;
                foreach (var t in step.AddTunings)
                    if (!acc.AddTunings.Contains(t, StringComparer.OrdinalIgnoreCase))
                        acc.AddTunings.Add(t);
                foreach (var m in step.AddMaps)
                    if (!acc.AddMaps.Any(x => string.Equals(x.Model, m.Model, StringComparison.OrdinalIgnoreCase)))
                        acc.AddMaps.Add(m);
                acc.Conflicts.AddRange(step.Conflicts);
            }
            return acc;
        }

        private static DoorNamedTuning CloneTuning(DoorNamedTuning nt)
        {
            var t = nt.Tuning;
            return new DoorNamedTuning
            {
                Name = nt.Name,
                Tuning = new DoorTuningParams
                {
                    AutoOpenVolumeOffsetX = t.AutoOpenVolumeOffsetX,
                    AutoOpenVolumeOffsetY = t.AutoOpenVolumeOffsetY,
                    AutoOpenVolumeOffsetZ = t.AutoOpenVolumeOffsetZ,
                    Flags = t.Flags.ToList(),
                    AutoOpenRadiusModifier = t.AutoOpenRadiusModifier,
                    AutoOpenRate = t.AutoOpenRate,
                    AutoOpenCosineAngleBetweenThreshold = t.AutoOpenCosineAngleBetweenThreshold,
                    AutoOpenCloseRateTaper = t.AutoOpenCloseRateTaper,
                    UseAutoOpenTriggerBox = t.UseAutoOpenTriggerBox,
                    CustomTriggerBox = t.CustomTriggerBox,
                    TriggerBoxMinX = t.TriggerBoxMinX,
                    TriggerBoxMinY = t.TriggerBoxMinY,
                    TriggerBoxMinZ = t.TriggerBoxMinZ,
                    TriggerBoxMaxX = t.TriggerBoxMaxX,
                    TriggerBoxMaxY = t.TriggerBoxMaxY,
                    TriggerBoxMaxZ = t.TriggerBoxMaxZ,
                    BreakableByVehicle = t.BreakableByVehicle,
                    BreakingImpulse = t.BreakingImpulse,
                    ShouldLatchShut = t.ShouldLatchShut,
                    MassMultiplier = t.MassMultiplier,
                    WeaponImpulseMultiplier = t.WeaponImpulseMultiplier,
                    RotationLimitAngle = t.RotationLimitAngle,
                    TorqueAngularVelocityLimit = t.TorqueAngularVelocityLimit,
                    StdDoorRotDir = t.StdDoorRotDir
                }
            };
        }
    }
}
