using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorEditor
{
    /// <summary>
    /// Editable doortuning.ymt document (CDoorTuningFile / PSO).
    /// Lives entirely in DoorEditor — Core is not modified.
    /// </summary>
    public sealed class DoorTuningDocument
    {
        public const string GameRelativePath = @"update\update.rpf\x64\levels\gta5\doortuning.ymt";

        public List<DoorNamedTuning> NamedTunings { get; set; } = new();
        public List<DoorModelMapping> ModelMappings { get; set; } = new();

        public static DoorTuningDocument FromYmt(YmtFile ymt)
        {
            if (ymt?.Pso == null)
                throw new InvalidDataException("doortuning.ymt did not load as PSO.");
            return FromXml(PsoXml.GetXml(ymt.Pso));
        }

        public static DoorTuningDocument FromXml(string xml)
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root ?? throw new InvalidDataException("Empty door tuning XML.");
            var result = new DoorTuningDocument();

            foreach (var item in root.Element("NamedTuningArray")?.Elements("Item") ?? Enumerable.Empty<XElement>())
            {
                result.NamedTunings.Add(new DoorNamedTuning
                {
                    Name = item.Element("Name")?.Value ?? string.Empty,
                    Tuning = DoorTuningParams.FromXml(item.Element("Tuning"))
                });
            }

            foreach (var item in root.Element("ModelToTuneMapping")?.Elements("Item") ?? Enumerable.Empty<XElement>())
            {
                result.ModelMappings.Add(new DoorModelMapping
                {
                    ModelName = item.Element("ModelName")?.Value ?? string.Empty,
                    TuningName = item.Element("TuningName")?.Value ?? string.Empty
                });
            }

            return result;
        }

        public string ToXml()
        {
            // FiveM Meta XML shape matches Hedgehog doortuning-example
            // (https://github.com/Hedgehog-Technologies/doortuning-example):
            // <Tuning> without type=, arrays with itemType hashes, LF line endings.
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            sb.Append("<CDoorTuningFile>\n");
            sb.Append(" <NamedTuningArray itemType=\"hash_243B5E8B\">\n");
            foreach (var nt in NamedTunings)
            {
                sb.Append("  <Item>\n");
                sb.Append($"   <Name>{Escape(nt.Name)}</Name>\n");
                sb.Append("   <Tuning>\n");
                (nt.Tuning ?? new DoorTuningParams()).WriteXml(sb, "    ");
                sb.Append("   </Tuning>\n");
                sb.Append("  </Item>\n");
            }
            sb.Append(" </NamedTuningArray>\n");
            sb.Append(" <ModelToTuneMapping itemType=\"hash_37ADF737\">\n");
            foreach (var map in ModelMappings)
            {
                sb.Append("  <Item>\n");
                sb.Append($"   <ModelName>{Escape(map.ModelName)}</ModelName>\n");
                sb.Append($"   <TuningName>{Escape(map.TuningName)}</TuningName>\n");
                sb.Append("  </Item>\n");
            }
            sb.Append(" </ModelToTuneMapping>\n");
            sb.Append("</CDoorTuningFile>\n");
            return sb.ToString();
        }

        /// <summary>Game-safe PSO bytes (16-byte block align + CHKS).</summary>
        public byte[] SaveBinary() => DoorTuningPsoExport.SaveBinary(this);

        public static DoorTuningDocument? LoadFromGame(RpfManager rpfMan)
        {
            if (rpfMan == null) return null;
            var ymt = rpfMan.GetFile<YmtFile>(GameRelativePath);
            if (ymt?.Pso == null) return null;
            return FromYmt(ymt);
        }

        private static string Escape(string s) =>
            System.Security.SecurityElement.Escape(s ?? string.Empty) ?? string.Empty;
    }

    public sealed class DoorNamedTuning
    {
        public string Name { get; set; } = string.Empty;
        public DoorTuningParams Tuning { get; set; } = new();
    }

    public sealed class DoorModelMapping
    {
        public string ModelName { get; set; } = string.Empty;
        public string TuningName { get; set; } = string.Empty;
    }

    public sealed class DoorTuningParams
    {
        public float AutoOpenVolumeOffsetX { get; set; }
        public float AutoOpenVolumeOffsetY { get; set; }
        public float AutoOpenVolumeOffsetZ { get; set; }
        public List<string> Flags { get; set; } = new();
        public float AutoOpenRadiusModifier { get; set; } = 1f;
        public float AutoOpenRate { get; set; } = 1f;
        public float AutoOpenCosineAngleBetweenThreshold { get; set; }
        public bool AutoOpenCloseRateTaper { get; set; }
        public bool UseAutoOpenTriggerBox { get; set; }
        public bool CustomTriggerBox { get; set; }
        public float TriggerBoxMinX { get; set; }
        public float TriggerBoxMinY { get; set; }
        public float TriggerBoxMinZ { get; set; }
        public float TriggerBoxMaxX { get; set; }
        public float TriggerBoxMaxY { get; set; }
        public float TriggerBoxMaxZ { get; set; }
        public bool BreakableByVehicle { get; set; }
        public float BreakingImpulse { get; set; }
        public bool ShouldLatchShut { get; set; }
        public float MassMultiplier { get; set; } = 1f;
        public float WeaponImpulseMultiplier { get; set; } = 1f;
        public float RotationLimitAngle { get; set; }
        public float TorqueAngularVelocityLimit { get; set; }
        public string StdDoorRotDir { get; set; } = "StdDoorOpenBothDir";

        public static DoorTuningParams FromXml(XElement? el)
        {
            var t = new DoorTuningParams();
            if (el == null) return t;

            var offset = el.Element("AutoOpenVolumeOffset");
            if (offset != null)
            {
                t.AutoOpenVolumeOffsetX = AttrF(offset, "x");
                t.AutoOpenVolumeOffsetY = AttrF(offset, "y");
                t.AutoOpenVolumeOffsetZ = AttrF(offset, "z");
            }

            var flagsEl = el.Element("Flags");
            if (flagsEl != null)
            {
                t.Flags = flagsEl.Value
                    .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
            }

            t.AutoOpenRadiusModifier = ChildF(el, "AutoOpenRadiusModifier", 1f);
            t.AutoOpenRate = ChildF(el, "AutoOpenRate", 1f);
            t.AutoOpenCosineAngleBetweenThreshold = ChildF(el, "AutoOpenCosineAngleBetweenThreshold");
            t.AutoOpenCloseRateTaper = ChildB(el, "AutoOpenCloseRateTaper");
            t.UseAutoOpenTriggerBox = ChildB(el, "UseAutoOpenTriggerBox");
            t.CustomTriggerBox = ChildB(el, "CustomTriggerBox");

            var box = el.Element("TriggerBoxMinMax");
            var min = box?.Element("min") ?? box?.Element("Min");
            var max = box?.Element("max") ?? box?.Element("Max");
            if (min != null)
            {
                t.TriggerBoxMinX = AttrF(min, "x");
                t.TriggerBoxMinY = AttrF(min, "y");
                t.TriggerBoxMinZ = AttrF(min, "z");
            }
            if (max != null)
            {
                t.TriggerBoxMaxX = AttrF(max, "x");
                t.TriggerBoxMaxY = AttrF(max, "y");
                t.TriggerBoxMaxZ = AttrF(max, "z");
            }

            t.BreakableByVehicle = ChildB(el, "BreakableByVehicle");
            t.BreakingImpulse = ChildF(el, "BreakingImpulse");
            t.ShouldLatchShut = ChildB(el, "ShouldLatchShut");
            t.MassMultiplier = ChildF(el, "MassMultiplier", 1f);
            t.WeaponImpulseMultiplier = ChildF(el, "WeaponImpulseMultiplier", 1f);
            t.RotationLimitAngle = ChildF(el, "RotationLimitAngle");
            t.TorqueAngularVelocityLimit = ChildF(el, "TorqueAngularVelocityLimit");
            t.StdDoorRotDir = el.Element("StdDoorRotDir")?.Value?.Trim() ?? "StdDoorOpenBothDir";
            if (string.IsNullOrEmpty(t.StdDoorRotDir))
                t.StdDoorRotDir = "StdDoorOpenBothDir";

            return t;
        }

        public void WriteXml(StringBuilder sb, string indent)
        {
            string F(float v) => FloatUtil.ToString(v);
            void L(string line) => sb.Append(line).Append('\n');
            L($"{indent}<AutoOpenVolumeOffset x=\"{F(AutoOpenVolumeOffsetX)}\" y=\"{F(AutoOpenVolumeOffsetY)}\" z=\"{F(AutoOpenVolumeOffsetZ)}\" />");
            if (Flags.Count == 0)
                L($"{indent}<Flags />");
            else
                L($"{indent}<Flags>{string.Join(" ", Flags)}</Flags>");
            L($"{indent}<AutoOpenRadiusModifier value=\"{F(AutoOpenRadiusModifier)}\" />");
            L($"{indent}<AutoOpenRate value=\"{F(AutoOpenRate)}\" />");
            L($"{indent}<AutoOpenCosineAngleBetweenThreshold value=\"{F(AutoOpenCosineAngleBetweenThreshold)}\" />");
            L($"{indent}<AutoOpenCloseRateTaper value=\"{AutoOpenCloseRateTaper.ToString().ToLowerInvariant()}\" />");
            L($"{indent}<UseAutoOpenTriggerBox value=\"{UseAutoOpenTriggerBox.ToString().ToLowerInvariant()}\" />");
            L($"{indent}<CustomTriggerBox value=\"{CustomTriggerBox.ToString().ToLowerInvariant()}\" />");
            L($"{indent}<TriggerBoxMinMax>");
            L($"{indent} <min x=\"{F(TriggerBoxMinX)}\" y=\"{F(TriggerBoxMinY)}\" z=\"{F(TriggerBoxMinZ)}\" />");
            L($"{indent} <max x=\"{F(TriggerBoxMaxX)}\" y=\"{F(TriggerBoxMaxY)}\" z=\"{F(TriggerBoxMaxZ)}\" />");
            L($"{indent}</TriggerBoxMinMax>");
            L($"{indent}<BreakableByVehicle value=\"{BreakableByVehicle.ToString().ToLowerInvariant()}\" />");
            L($"{indent}<BreakingImpulse value=\"{F(BreakingImpulse)}\" />");
            L($"{indent}<ShouldLatchShut value=\"{ShouldLatchShut.ToString().ToLowerInvariant()}\" />");
            L($"{indent}<MassMultiplier value=\"{F(MassMultiplier)}\" />");
            L($"{indent}<WeaponImpulseMultiplier value=\"{F(WeaponImpulseMultiplier)}\" />");
            L($"{indent}<RotationLimitAngle value=\"{F(RotationLimitAngle)}\" />");
            L($"{indent}<TorqueAngularVelocityLimit value=\"{F(TorqueAngularVelocityLimit)}\" />");
            L($"{indent}<StdDoorRotDir>{StdDoorRotDir}</StdDoorRotDir>");
        }

        private static float AttrF(XElement el, string name)
        {
            var a = el.Attribute(name)?.Value;
            return float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }

        private static float ChildF(XElement el, string name, float def = 0f)
        {
            var child = el.Element(name);
            if (child == null) return def;
            var a = child.Attribute("value")?.Value ?? child.Value;
            return float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
        }

        private static bool ChildB(XElement el, string name)
        {
            var child = el.Element(name);
            if (child == null) return false;
            var a = child.Attribute("value")?.Value ?? child.Value;
            return string.Equals(a, "true", StringComparison.OrdinalIgnoreCase) || a == "1";
        }
    }
}
