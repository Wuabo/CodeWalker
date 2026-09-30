using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorEditor
{
    /// <summary>
    /// One door model → DoorAudioSettings + DASL link (audDoorAudioEntity::InitDoor).
    /// </summary>
    public sealed class DoorAudioAssignment
    {
        public string ModelName { get; set; } = string.Empty;
        public string Sounds { get; set; } = string.Empty;
        public string TuningParams { get; set; } = string.Empty;
        public float MaxOcclusion { get; set; } = 0.7f;
        public string PresetLabel { get; set; } = string.Empty;

        public string NormalizedModel =>
            (ModelName ?? string.Empty).Trim().ToLowerInvariant();

        public uint ModelHash =>
            string.IsNullOrEmpty(NormalizedModel) ? 0u : JenkHash.GenHashLowerInvariant(NormalizedModel);

        /// <summary>DoorAudioSettings &lt;Name&gt; — model archetype only (no d_ prefix).</summary>
        public string SettingsName =>
            string.IsNullOrEmpty(NormalizedModel) ? string.Empty : NormalizedModel;

        /// <summary>
        /// DoorAudioSettingsLink &lt;Name&gt;: dasl_ + Jenkins hash of the model (hex, no 0x), all lowercase.
        /// Example: wuabo_storage_g_door → dasl_9a333c1b
        /// </summary>
        public string LinkName =>
            ModelHash == 0 ? string.Empty : "dasl_" + ModelHash.ToString("x8", CultureInfo.InvariantCulture);
    }

    /// <summary>One row from availableDoorSound catalog (friendly name → Sounds / TuningParams).</summary>
    public sealed class DoorAudioPreset
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Sounds { get; set; } = string.Empty;
        public string TuningParams { get; set; } = string.Empty;
        public float MaxOcclusion { get; set; } = 0.7f;

        public override string ToString() => Label;
    }

    /// <summary>
    /// Dat151 door audio document + preset catalog (JSON availableDoorSound).
    /// Sounds live in the preset list — picking a preset fills Sounds / TuningParams.
    /// </summary>
    public sealed class DoorAudioDocument
    {
        /// <summary>Common CodeWalker / community Dat151 XML version.</summary>
        public const uint DefaultDat151Version = 9458585;

        public List<DoorAudioAssignment> Assignments { get; } = new();
        public List<DoorAudioPreset> Catalog { get; } = new();

        public DoorAudioDocument()
        {
            Catalog.AddRange(LoadDefaultCatalog());
            ResolveCatalogNames();
        }

        public static List<DoorAudioPreset> LoadDefaultCatalog()
        {
            foreach (var path in DefaultCatalogPaths())
            {
                if (!File.Exists(path)) continue;
                try { return ParseCatalogJson(File.ReadAllText(path)); }
                catch { /* try next */ }
            }
            return FallbackCatalog();
        }

        private static IEnumerable<string> DefaultCatalogPaths()
        {
            var baseDir = AppContext.BaseDirectory;
            yield return Path.Combine(baseDir, "Templates", "door-audio-presets.json");
            yield return Path.Combine(baseDir, "door-audio-presets.json");
            var asm = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? baseDir;
            yield return Path.Combine(asm, "Templates", "door-audio-presets.json");
        }

        public static List<DoorAudioPreset> ParseCatalogJson(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement items;
            if (root.ValueKind == JsonValueKind.Array)
                items = root;
            else if (root.TryGetProperty("availableDoorSound", out var a))
                items = a;
            else if (root.TryGetProperty("entries", out var e))
                items = e;
            else if (root.TryGetProperty("audioSettings", out var s))
                items = s;
            else
                throw new InvalidDataException("Expected availableDoorSound array.");

            var list = new List<DoorAudioPreset>();
            int i = 0;
            foreach (var row in items.EnumerateArray())
            {
                var name = row.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var sounds = row.TryGetProperty("Sounds", out var so) ? so.GetString() ?? "" : "";
                var tuning = row.TryGetProperty("TuningParams", out var tu) ? tu.GetString() ?? "" : "";
                float max = 0.7f;
                if (row.TryGetProperty("MaxOcclusion", out var mo))
                {
                    if (mo.ValueKind == JsonValueKind.Number) max = mo.GetSingle();
                    else if (mo.ValueKind == JsonValueKind.String &&
                             float.TryParse(mo.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var mf))
                        max = mf;
                }
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(sounds) || string.IsNullOrWhiteSpace(tuning))
                    throw new InvalidDataException($"Preset #{i + 1} needs name, Sounds, and TuningParams.");
                list.Add(new DoorAudioPreset
                {
                    Id = "preset:" + i,
                    Label = name.Trim(),
                    Sounds = sounds.Trim(),
                    TuningParams = tuning.Trim(),
                    MaxOcclusion = Math.Clamp(max, 0f, 1f),
                });
                i++;
            }
            if (list.Count == 0)
                throw new InvalidDataException("Catalog must include at least one preset.");
            return list;
        }

        public static string CatalogToJson(IEnumerable<DoorAudioPreset> catalog)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"availableDoorSound\": [");
            var items = catalog.ToList();
            for (int i = 0; i < items.Count; i++)
            {
                var p = items[i];
                var comma = i < items.Count - 1 ? "," : "";
                sb.AppendLine(
                    $"    {{ \"name\": {JsonEscape(p.Label)}, \"Sounds\": {JsonEscape(p.Sounds)}, \"TuningParams\": {JsonEscape(p.TuningParams)}, \"MaxOcclusion\": {p.MaxOcclusion.ToString(CultureInfo.InvariantCulture)} }}{comma}");
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string JsonEscape(string s) =>
            "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        /// <summary>Minimal fallback if Templates/door-audio-presets.json is missing.</summary>
        private static List<DoorAudioPreset> FallbackCatalog() =>
        [
            new() { Id = "preset:0", Label = "Normal Gate", Sounds = "door_garage", TuningParams = "dtp_mp_garage_door", MaxOcclusion = 0.7f },
            new() { Id = "preset:1", Label = "Pushed Door", Sounds = "door_swing_exit_wood_no_mech", TuningParams = "dtp_default_swing", MaxOcclusion = 0.7f },
            new() { Id = "preset:2", Label = "Sliding Door", Sounds = "door_slide_manual", TuningParams = "dtp_sliding_door_interior", MaxOcclusion = 0.7f },
            new() { Id = "preset:3", Label = "Up-Down Gate", Sounds = "door_garage_ls_customs", TuningParams = "dtp_default_sliding_vertical", MaxOcclusion = 0.7f },
        ];

        public void ReplaceCatalog(IEnumerable<DoorAudioPreset> presets)
        {
            Catalog.Clear();
            Catalog.AddRange(presets);
            ResolveCatalogNames();
        }

        /// <summary>
        /// Resolve Sounds / TuningParams hash_XXXXXXXX (or decimal Jenk fallbacks) to
        /// real names via JenkIndex / MetaNames when known. Catalog <see cref="DoorAudioPreset.Label"/>
        /// is never changed — that is the friendly availableDoorSound name.
        /// </summary>
        public void ResolveCatalogNames()
        {
            foreach (var p in Catalog)
            {
                // Keep Label as the original friendly name from JSON / catalog.
                p.Sounds = ResolveAudioName(p.Sounds);
                p.TuningParams = ResolveAudioName(p.TuningParams);
                if (!string.IsNullOrWhiteSpace(p.Sounds)) JenkIndex.Ensure(p.Sounds);
                if (!string.IsNullOrWhiteSpace(p.TuningParams)) JenkIndex.Ensure(p.TuningParams);
            }
        }

        public static string ResolveAudioName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;
            var v = value.Trim();

            // JenkIndex.GetString fallback is decimal digits — treat as unresolved.
            if (IsNumericHashLabel(v))
                v = "hash_" + uint.Parse(v, CultureInfo.InvariantCulture).ToString("x8", CultureInfo.InvariantCulture);

            // Already a real name — register and keep.
            if (!v.StartsWith("hash_", StringComparison.OrdinalIgnoreCase))
            {
                JenkIndex.Ensure(v);
                JenkIndex.Ensure(v.ToLowerInvariant());
                return v;
            }

            var h = XmlRel.GetHash(v);
            var named = JenkIndex.TryGetString(h);
            if (!string.IsNullOrEmpty(named) && !IsNumericHashLabel(named)) return named;
            if (MetaNames.TryGetString(h, out named) && !string.IsNullOrEmpty(named) && !IsNumericHashLabel(named))
                return named;
            return v;
        }

        /// <summary>
        /// True when value is empty or pure decimal digits (JenkIndex unresolved display).
        /// </summary>
        public static bool IsNumericHashLabel(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return true;
            s = s.Trim();
            if (s.Length == 0) return true;
            for (int i = 0; i < s.Length; i++)
                if (!char.IsDigit(s[i])) return false;
            return true;
        }

        public void SyncModelsFromMappings(IEnumerable<string> modelNames, DoorAudioPreset? defaultPreset = null)
        {
            var preset = defaultPreset ?? Catalog.FirstOrDefault();
            var existing = Assignments.ToDictionary(a => a.NormalizedModel, StringComparer.OrdinalIgnoreCase);
            foreach (var raw in modelNames)
            {
                var model = (raw ?? string.Empty).Trim();
                if (model.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
                    model = model[..^4];
                if (string.IsNullOrEmpty(model)) continue;
                var key = model.ToLowerInvariant();
                if (existing.ContainsKey(key)) continue;
                var a = new DoorAudioAssignment { ModelName = key };
                if (preset != null) ApplyPreset(a, preset);
                Assignments.Add(a);
                existing[key] = a;
            }
        }

        public static void ApplyPreset(DoorAudioAssignment a, DoorAudioPreset preset)
        {
            a.PresetLabel = preset.Label;
            a.Sounds = preset.Sounds ?? string.Empty;
            a.TuningParams = preset.TuningParams ?? string.Empty;
            a.MaxOcclusion = preset.MaxOcclusion > 0f ? preset.MaxOcclusion : 0.7f;
        }

        public string ToRelXml()
        {
            var rel = BuildRelFile();
            return RelXml.GetXml(rel);
        }

        public RelFile BuildRelFile()
        {
            var rel = new RelFile
            {
                RelType = RelDatFileType.Dat151,
                DataUnkVal = DefaultDat151Version,
            };

            foreach (var a in Assignments.Where(x => !string.IsNullOrEmpty(x.NormalizedModel)))
            {
                if (string.IsNullOrWhiteSpace(a.Sounds))
                    throw new InvalidOperationException($"Door '{a.NormalizedModel}' has empty Sounds — pick a preset.");
                if (string.IsNullOrWhiteSpace(a.TuningParams))
                    throw new InvalidOperationException($"Door '{a.NormalizedModel}' has empty TuningParams — pick a preset.");

                JenkIndex.Ensure(a.SettingsName);
                JenkIndex.Ensure(a.Sounds.Trim());
                JenkIndex.Ensure(a.TuningParams.Trim());
                JenkIndex.Ensure(a.LinkName);

                rel.AddRelData(new Dat151DoorAudioSettings(rel)
                {
                    Name = a.SettingsName,
                    NameHash = XmlRel.GetHash(a.SettingsName),
                    NameTableOffset = 0,
                    Sounds = XmlRel.GetHash(a.Sounds.Trim()),
                    TuningParams = XmlRel.GetHash(a.TuningParams.Trim()),
                    MaxOcclusion = a.MaxOcclusion,
                });

                rel.AddRelData(new Dat151DoorAudioSettingsLink(rel)
                {
                    Name = a.LinkName,
                    NameHash = XmlRel.GetHash(a.LinkName),
                    NameTableOffset = 0,
                    Door = XmlRel.GetHash(a.SettingsName),
                });
            }

            return rel;
        }

        public void SaveRelXml(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, ToRelXml(), new UTF8Encoding(false));
        }

        /// <summary>
        /// CodeWalker-style .nametable: null-separated UTF-8 names (needed so Rel hashes
        /// like model / dasl_* resolve back to strings when opening in CodeWalker).
        /// </summary>
        public void SaveNameTable(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var bytes = Encoding.UTF8.GetBytes(BuildNameTableText());
            File.WriteAllBytes(path, bytes);
        }

        /// <summary>
        /// Sidecar path next to Rel XML, e.g. game.dat151.rel.xml → game.dat151.nametable
        /// </summary>
        public static string SuggestNameTablePath(string relXmlPath)
        {
            var dir = Path.GetDirectoryName(relXmlPath) ?? "";
            var file = Path.GetFileName(relXmlPath) ?? "game.dat151.rel.xml";
            string stem;
            if (file.EndsWith(".dat151.rel.xml", StringComparison.OrdinalIgnoreCase))
                stem = file[..^".rel.xml".Length]; // game.dat151
            else if (file.EndsWith(".rel.xml", StringComparison.OrdinalIgnoreCase))
                stem = file[..^".rel.xml".Length];
            else if (file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                stem = file[..^4];
            else
                stem = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(stem)) stem = "game.dat151";
            if (!stem.EndsWith(".dat151", StringComparison.OrdinalIgnoreCase) &&
                !stem.EndsWith(".nametable", StringComparison.OrdinalIgnoreCase))
            {
                // keep stem as-is (e.g. "game.dat151" or "door_audio")
            }
            return Path.Combine(dir, stem + ".nametable");
        }

        public string BuildNameTableText()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in Assignments)
            {
                if (string.IsNullOrEmpty(a.NormalizedModel)) continue;
                names.Add(a.SettingsName); // model archetype
                names.Add(a.LinkName);     // dasl_xxxxxxxx (matches Rel export)
                AddNameIfUseful(names, a.Sounds);
                AddNameIfUseful(names, a.TuningParams);
            }
            if (names.Count == 0) return "";
            return string.Join("\0", names) + "\0";
        }

        private static void AddNameIfUseful(SortedSet<string> names, string? value)
        {
            var v = (value ?? "").Trim();
            if (string.IsNullOrEmpty(v)) return;
            if (IsNumericHashLabel(v)) return;
            if (v.StartsWith("hash_", StringComparison.OrdinalIgnoreCase)) return;
            names.Add(v);
        }

        public static DoorAudioDocument FromRelXml(string xml)
        {
            var rel = XmlRel.GetRel(xml);
            var doc = new DoorAudioDocument();
            if (rel?.RelDatasSorted == null) return doc;

            var settingsByHash = new Dictionary<uint, Dat151DoorAudioSettings>();
            foreach (var item in rel.RelDatasSorted)
            {
                if (item is Dat151DoorAudioSettings s)
                    settingsByHash[s.NameHash] = s;
            }

            doc.Assignments.Clear();
            foreach (var item in rel.RelDatasSorted)
            {
                if (item is not Dat151DoorAudioSettingsLink link) continue;
                if (!settingsByHash.TryGetValue(link.Door, out var settings)) continue;

                var model = ModelFromSettingsOrLink(settings.Name, link.Name);
                doc.Assignments.Add(new DoorAudioAssignment
                {
                    ModelName = model,
                    Sounds = HashToString(settings.Sounds),
                    TuningParams = HashToString(settings.TuningParams),
                    MaxOcclusion = settings.MaxOcclusion,
                    PresetLabel = MatchPresetLabel(doc.Catalog, HashToString(settings.Sounds), HashToString(settings.TuningParams)),
                });
            }

            return doc;
        }

        public static DoorAudioDocument LoadRelXml(string path) =>
            FromRelXml(File.ReadAllText(path));

        public static string MatchPresetLabel(IEnumerable<DoorAudioPreset> catalog, string sounds, string tuning)
        {
            foreach (var p in catalog)
            {
                if (string.Equals(p.Sounds, sounds, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.TuningParams, tuning, StringComparison.OrdinalIgnoreCase))
                    return p.Label;
            }
            return "";
        }

        private static string ModelFromSettingsOrLink(string? settingsName, string? linkName)
        {
            // Legacy exports used d_model as SettingsName; current format is the model itself.
            if (!string.IsNullOrEmpty(settingsName) &&
                settingsName.StartsWith("d_", StringComparison.OrdinalIgnoreCase) &&
                settingsName.Length > 2)
                return settingsName[2..].ToLowerInvariant();

            if (!string.IsNullOrEmpty(settingsName))
                return settingsName.ToLowerInvariant();

            if (!string.IsNullOrEmpty(linkName) &&
                linkName.StartsWith("dasl_", StringComparison.OrdinalIgnoreCase))
                return "model_" + linkName[5..].ToLowerInvariant();

            return string.Empty;
        }

        private static string HashToString(MetaHash h)
        {
            return ResolveAudioName(RelXml.HashString(h));
        }
    }
}
