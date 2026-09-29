using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace CodeWalker.GameFiles
{
    /// <summary>
    /// Builds / edits Dat151 door audio entries using CodeWalker REL types
    /// (DoorAudioSettings + DoorAudioSettingsLink), not legacy Door/DoorModel XML.
    /// </summary>
    public class DoorAudioDocument
    {
        public const uint DefaultDat151Version = 7126027u;

        public uint Version { get; set; } = DefaultDat151Version;
        public List<DoorAudioEntry> Doors { get; set; } = new();

        public static DoorAudioDocument FromRel(RelFile rel)
        {
            var doc = new DoorAudioDocument
            {
                Version = rel.DataUnkVal != 0 ? rel.DataUnkVal : DefaultDat151Version
            };

            var settings = rel.RelDatas
                .OfType<Dat151DoorAudioSettings>()
                .ToDictionary(s => s.NameHash.Hash, s => s);

            var links = rel.RelDatas.OfType<Dat151DoorAudioSettingsLink>().ToList();

            foreach (var setting in settings.Values)
            {
                var name = setting.Name;
                if (string.IsNullOrEmpty(name))
                    name = JenkIndex.TryGetString(setting.NameHash.Hash);
                if (string.IsNullOrEmpty(name))
                    name = $"hash_{setting.NameHash.Hex}";

                // Prefer display name without d_ prefix for the UI when present
                var display = name.StartsWith("d_", StringComparison.OrdinalIgnoreCase)
                    ? name.Substring(2)
                    : name;

                var link = links.FirstOrDefault(l => l.Door.Hash == setting.NameHash.Hash);
                string linkName = string.Empty;
                if (link != null)
                {
                    linkName = link.Name ?? string.Empty;
                    if (string.IsNullOrEmpty(linkName))
                        linkName = JenkIndex.TryGetString(link.NameHash.Hash) ?? string.Empty;
                }
                doc.Doors.Add(new DoorAudioEntry
                {
                    Name = display,
                    Sounds = HashLabel(setting.Sounds),
                    TuningParams = HashLabel(setting.TuningParams),
                    MaxOcclusion = setting.MaxOcclusion,
                    LinkName = linkName
                });
            }

            return doc;
        }

        public static DoorAudioDocument FromXml(string xml)
        {
            if (LooksLikeLegacyDoorXml(xml))
                return FromLegacyDoorXml(xml);
            var rel = XmlRel.GetRel(xml);
            return FromRel(rel);
        }

        /// <summary>
        /// Imports older tools' Dat151 XML that used Item type="Door" / "DoorModel"
        /// and maps them onto CodeWalker DoorAudioSettings / DoorAudioSettingsLink.
        /// </summary>
        public static DoorAudioDocument FromLegacyDoorXml(string xml)
        {
            var xdoc = new XmlDocument();
            xdoc.LoadXml(xml);
            var doc = new DoorAudioDocument();

            var verNode = xdoc.SelectSingleNode("//Dat151/Version") ?? xdoc.SelectSingleNode("//Version");
            if (verNode != null)
            {
                var v = verNode.Attributes?["value"]?.Value;
                if (uint.TryParse(v, out var version))
                    doc.Version = version;
            }

            var items = xdoc.SelectNodes("//Items/Item") ?? xdoc.SelectNodes("//Item");
            if (items == null) return doc;

            var links = new List<(string LinkName, string DoorRef)>();

            foreach (XmlNode item in items)
            {
                var type = item.Attributes?["type"]?.Value ?? string.Empty;
                if (string.Equals(type, "Door", StringComparison.OrdinalIgnoreCase))
                {
                    var rawName = item.SelectSingleNode("Name")?.InnerText?.Trim() ?? string.Empty;
                    var display = rawName;
                    if (display.StartsWith("d_", StringComparison.OrdinalIgnoreCase))
                        display = display.Substring(2);
                    if (display.StartsWith("hash_", StringComparison.OrdinalIgnoreCase))
                        display = rawName; // keep hash label as name

                    var sounds = item.SelectSingleNode("SoundSet")?.InnerText?.Trim()
                        ?? item.SelectSingleNode("Sounds")?.InnerText?.Trim()
                        ?? "null";
                    var tuning = item.SelectSingleNode("Params")?.InnerText?.Trim()
                        ?? item.SelectSingleNode("TuningParams")?.InnerText?.Trim()
                        ?? "null";
                    if (string.IsNullOrEmpty(sounds)) sounds = "null";
                    if (string.IsNullOrEmpty(tuning)) tuning = "null";

                    float occ = 0.7f;
                    var unk = item.SelectSingleNode("Unk1") ?? item.SelectSingleNode("MaxOcclusion");
                    var occStr = unk?.Attributes?["value"]?.Value ?? unk?.InnerText;
                    if (float.TryParse(occStr, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                        occ = parsed;

                    doc.Doors.Add(new DoorAudioEntry
                    {
                        Name = display,
                        Sounds = sounds,
                        TuningParams = tuning,
                        MaxOcclusion = occ
                    });
                }
                else if (string.Equals(type, "DoorModel", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(type, "DoorAudioSettingsLink", StringComparison.OrdinalIgnoreCase))
                {
                    var linkName = item.SelectSingleNode("Name")?.InnerText?.Trim() ?? string.Empty;
                    var doorRef = item.SelectSingleNode("Door")?.InnerText?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(doorRef))
                        links.Add((linkName, doorRef));
                }
            }

            foreach (var (linkName, doorRef) in links)
            {
                var key = doorRef.StartsWith("d_", StringComparison.OrdinalIgnoreCase)
                    ? doorRef.Substring(2)
                    : doorRef;
                var door = doc.Doors.FirstOrDefault(d =>
                    string.Equals(d.Name, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(NormalizeDoorName(d.Name), doorRef, StringComparison.OrdinalIgnoreCase));
                if (door != null && !string.IsNullOrEmpty(linkName))
                    door.LinkName = linkName;
            }

            return doc;
        }

        public static bool LooksLikeLegacyDoorXml(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return false;
            return xml.Contains("type=\"Door\"", StringComparison.OrdinalIgnoreCase)
                   || xml.Contains("type='Door'", StringComparison.OrdinalIgnoreCase)
                   || xml.Contains("type=\"DoorModel\"", StringComparison.OrdinalIgnoreCase);
        }

        public RelFile ToRel()
        {
            var xml = ToRelXml();
            return XmlRel.GetRel(xml);
        }

        public string ToRelXml()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<Dat151>");
            sb.AppendLine($" <Version value=\"{Version}\" />");
            sb.AppendLine(" <Items>");

            foreach (var door in Doors)
            {
                var doorName = NormalizeDoorName(door.Name);
                JenkIndex.Ensure(doorName);
                sb.AppendLine($"  <Item type=\"DoorAudioSettings\" ntOffset=\"0\">");
                sb.AppendLine($"   <Name>{Escape(doorName)}</Name>");
                sb.AppendLine($"   <Sounds>{Escape(door.Sounds)}</Sounds>");
                sb.AppendLine($"   <TuningParams>{Escape(door.TuningParams)}</TuningParams>");
                sb.AppendLine($"   <MaxOcclusion value=\"{FloatUtil.ToString(door.MaxOcclusion)}\" />");
                sb.AppendLine("  </Item>");
            }

            foreach (var door in Doors)
            {
                var doorName = NormalizeDoorName(door.Name);
                var modelKey = door.Name.Trim().ToLowerInvariant();
                var hash = JenkHash.GenHash(modelKey);
                var linkName = string.IsNullOrWhiteSpace(door.LinkName)
                    ? $"dasl_{hash:x8}"
                    : door.LinkName.Trim();
                JenkIndex.Ensure(linkName);
                JenkIndex.Ensure(doorName);
                sb.AppendLine($"  <Item type=\"DoorAudioSettingsLink\" ntOffset=\"0\">");
                sb.AppendLine($"   <Name>{Escape(linkName)}</Name>");
                sb.AppendLine($"   <Door>{Escape(doorName)}</Door>");
                sb.AppendLine("  </Item>");
            }

            sb.AppendLine(" </Items>");
            sb.AppendLine("</Dat151>");
            return sb.ToString();
        }

        public byte[] SaveRelBytes()
        {
            return ToRel().Save();
        }

        public string BuildNameTableText()
        {
            var sb = new StringBuilder();
            foreach (var door in Doors)
            {
                sb.AppendLine(NormalizeDoorName(door.Name));
            }
            return sb.ToString();
        }

        public void Export(string relPath, string? nametablePath = null)
        {
            File.WriteAllBytes(relPath, SaveRelBytes());
            nametablePath ??= Path.ChangeExtension(relPath, null);
            if (nametablePath.EndsWith(".rel", StringComparison.OrdinalIgnoreCase))
                nametablePath = nametablePath.Substring(0, nametablePath.Length - 4);
            if (!nametablePath.EndsWith(".nametable", StringComparison.OrdinalIgnoreCase))
                nametablePath += ".nametable";
            File.WriteAllText(nametablePath, BuildNameTableText(), Encoding.ASCII);
        }

        public void ExportXml(string xmlPath)
        {
            File.WriteAllText(xmlPath, ToRelXml(), Encoding.UTF8);
        }

        public static string NormalizeDoorName(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.StartsWith("d_", StringComparison.OrdinalIgnoreCase))
                return name;
            return "d_" + name;
        }

        /// <summary>Jenkins hash of the model/door key (lowercase name without d_), used for dasl_&lt;hash&gt;.</summary>
        public static uint GetModelHash(string doorName)
        {
            var key = (doorName ?? string.Empty).Trim();
            if (key.StartsWith("d_", StringComparison.OrdinalIgnoreCase))
                key = key.Substring(2);
            return JenkHash.GenHash(key.ToLowerInvariant());
        }

        public static string GetAutoLinkName(string doorName) =>
            $"dasl_{GetModelHash(doorName):x8}";

        public static string FormatModelHashHex(string doorName) =>
            GetModelHash(doorName).ToString("x8");

        /// <summary>
        /// Scans every packed *.dat151.rel under the game folder for DoorAudioSettings
        /// (+ matching DoorAudioSettingsLink). Later DLC/update entries override earlier ones by hash.
        /// </summary>
        public static List<DoorAudioGameEntry> LoadAllFromGame(RpfManager rpfMan, Action<string>? status = null)
        {
            if (rpfMan?.AllRpfs == null)
                return new List<DoorAudioGameEntry>();

            var settingsByHash = new Dictionary<uint, DoorAudioGameEntry>();
            var linksByDoorHash = new Dictionary<uint, List<(string LinkName, string SourceFile)>>();

            foreach (var rpf in rpfMan.AllRpfs)
            {
                if (rpf.AllEntries == null) continue;
                foreach (var entry in rpf.AllEntries)
                {
                    if (entry is not RpfFileEntry fentry) continue;
                    if (!entry.NameLower.EndsWith(".dat151.rel")) continue;

                    status?.Invoke("Scanning " + (entry.Path ?? entry.Name));
                    RelFile? rel;
                    try
                    {
                        rel = rpfMan.GetFile<RelFile>(fentry);
                    }
                    catch
                    {
                        continue;
                    }
                    if (rel?.RelDatas == null) continue;

                    var source = entry.Path ?? entry.Name ?? string.Empty;

                    foreach (var setting in rel.RelDatas.OfType<Dat151DoorAudioSettings>())
                    {
                        var name = setting.Name;
                        if (string.IsNullOrEmpty(name))
                            name = JenkIndex.TryGetString(setting.NameHash.Hash);
                        if (string.IsNullOrEmpty(name))
                            name = $"hash_{setting.NameHash.Hex}";

                        var display = name.StartsWith("d_", StringComparison.OrdinalIgnoreCase)
                            ? name.Substring(2)
                            : name;

                        settingsByHash[setting.NameHash.Hash] = new DoorAudioGameEntry
                        {
                            Name = display,
                            SettingsName = name,
                            Sounds = HashLabel(setting.Sounds),
                            TuningParams = HashLabel(setting.TuningParams),
                            MaxOcclusion = setting.MaxOcclusion,
                            SourceFile = source
                        };
                    }

                    foreach (var link in rel.RelDatas.OfType<Dat151DoorAudioSettingsLink>())
                    {
                        var doorHash = link.Door.Hash;
                        var linkName = link.Name;
                        if (string.IsNullOrEmpty(linkName))
                            linkName = JenkIndex.TryGetString(link.NameHash.Hash) ?? string.Empty;
                        if (!linksByDoorHash.TryGetValue(doorHash, out var list))
                        {
                            list = new List<(string, string)>();
                            linksByDoorHash[doorHash] = list;
                        }
                        list.Add((linkName, source));
                    }
                }
            }

            foreach (var kv in settingsByHash)
            {
                if (!linksByDoorHash.TryGetValue(kv.Key, out var links) || links.Count == 0)
                    continue;
                // Prefer last (DLC) link; keep all link names for UI
                kv.Value.LinkName = links[^1].LinkName;
                kv.Value.LinkNames = links.Select(l => l.LinkName).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
            }

            return settingsByHash.Values
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<DoorAudioPreset> PresetsFromGameEntries(IEnumerable<DoorAudioGameEntry> entries)
        {
            var presets = new List<DoorAudioPreset>
            {
                new() { Name = "No Sound", Sounds = "null", TuningParams = "null", MaxOcclusion = 0f }
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "No Sound" };
            foreach (var e in entries)
            {
                var label = string.IsNullOrEmpty(e.SettingsName) ? e.Name : e.SettingsName;
                if (!seen.Add(label)) continue;
                presets.Add(new DoorAudioPreset
                {
                    Name = label,
                    Sounds = e.Sounds,
                    TuningParams = e.TuningParams,
                    MaxOcclusion = e.MaxOcclusion
                });
            }
            return presets;
        }

        /// <summary>
        /// Merges curated (friendly-named) presets first, then unique game-derived presets.
        /// </summary>
        public static List<DoorAudioPreset> MergePresets(IEnumerable<DoorAudioPreset> curated, IEnumerable<DoorAudioGameEntry> game)
        {
            var result = new List<DoorAudioPreset>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in curated ?? Enumerable.Empty<DoorAudioPreset>())
            {
                if (string.IsNullOrWhiteSpace(p.Name) || !seen.Add(p.Name)) continue;
                result.Add(p);
            }
            if (!seen.Contains("No Sound"))
            {
                result.Insert(0, new DoorAudioPreset { Name = "No Sound", Sounds = "null", TuningParams = "null", MaxOcclusion = 0f });
                seen.Add("No Sound");
            }
            foreach (var e in game ?? Enumerable.Empty<DoorAudioGameEntry>())
            {
                var label = string.IsNullOrEmpty(e.SettingsName) ? e.Name : e.SettingsName;
                if (!seen.Add(label)) continue;
                result.Add(new DoorAudioPreset
                {
                    Name = label,
                    Sounds = e.Sounds,
                    TuningParams = e.TuningParams,
                    MaxOcclusion = e.MaxOcclusion
                });
            }
            return result;
        }

        private static string HashLabel(MetaHash h)
        {
            if (h.Hash == 0) return "null";
            var s = JenkIndex.TryGetString(h.Hash);
            if (!string.IsNullOrEmpty(s)) return s;
            return "hash_" + h.Hex;
        }

        private static string Escape(string s) =>
            System.Security.SecurityElement.Escape(s) ?? string.Empty;
    }

    public class DoorAudioEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Sounds { get; set; } = "null";
        public string TuningParams { get; set; } = "null";
        public float MaxOcclusion { get; set; } = 0.7f;
        public string LinkName { get; set; } = string.Empty;
    }

    public class DoorAudioGameEntry : DoorAudioEntry
    {
        public string SettingsName { get; set; } = string.Empty;
        public string SourceFile { get; set; } = string.Empty;
        public List<string> LinkNames { get; set; } = new();
    }

    public class DoorAudioPreset
    {
        public string Name { get; set; } = string.Empty;
        public string Sounds { get; set; } = "null";
        public string TuningParams { get; set; } = "null";
        public float MaxOcclusion { get; set; } = 0.7f;
    }
}
