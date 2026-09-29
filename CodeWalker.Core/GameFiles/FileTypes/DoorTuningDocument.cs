using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CodeWalker.GameFiles
{
    /// <summary>
    /// Editable door-tuning document backed by doortuning.ymt (PSO / CDoorTuningFile).
    /// Round-trips through PSO XML so schema stays aligned with PsoTypes.
    /// </summary>
    public class DoorTuningDocument
    {
        public const string GameRelativePath = @"update\update.rpf\x64\levels\gta5\doortuning.ymt";

        public List<DoorNamedTuning> NamedTunings { get; set; } = new();
        public List<DoorModelMapping> ModelMappings { get; set; } = new();

        public static DoorTuningDocument FromYmt(YmtFile ymt)
        {
            if (ymt?.Pso == null)
                throw new InvalidDataException("doortuning.ymt did not load as PSO.");

            var xml = PsoXml.GetXml(ymt.Pso);
            return FromXml(xml);
        }

        public static DoorTuningDocument FromXml(string xml)
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root ?? throw new InvalidDataException("Empty door tuning XML.");
            var result = new DoorTuningDocument();

            foreach (var item in root.Element("NamedTuningArray")?.Elements("Item") ?? Enumerable.Empty<XElement>())
            {
                var tuningEl = item.Element("Tuning");
                result.NamedTunings.Add(new DoorNamedTuning
                {
                    Name = item.Element("Name")?.Value ?? string.Empty,
                    Tuning = DoorTuningParams.FromXml(tuningEl)
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
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<CDoorTuningFile>");
            sb.AppendLine(" <NamedTuningArray>");
            foreach (var nt in NamedTunings)
            {
                sb.AppendLine("  <Item>");
                sb.AppendLine($"   <Name>{Escape(nt.Name)}</Name>");
                sb.AppendLine("   <Tuning type=\"CDoorTuning\">");
                nt.Tuning.WriteXml(sb, "    ");
                sb.AppendLine("   </Tuning>");
                sb.AppendLine("  </Item>");
            }
            sb.AppendLine(" </NamedTuningArray>");
            sb.AppendLine(" <ModelToTuneMapping>");
            foreach (var map in ModelMappings)
            {
                sb.AppendLine("  <Item>");
                sb.AppendLine($"   <ModelName>{Escape(map.ModelName)}</ModelName>");
                sb.AppendLine($"   <TuningName>{Escape(map.TuningName)}</TuningName>");
                sb.AppendLine("  </Item>");
            }
            sb.AppendLine(" </ModelToTuneMapping>");
            sb.AppendLine("</CDoorTuningFile>");
            return sb.ToString();
        }

        public byte[] Save()
        {
            var xdoc = new XmlDocument();
            xdoc.LoadXml(ToXml());
            var pso = XmlPso.GetPso(xdoc);
            return pso.Save();
        }

        public YmtFile ToYmt()
        {
            var ymt = new YmtFile();
            var bytes = Save();
            using var ms = new MemoryStream(bytes);
            ymt.Pso = new PsoFile();
            ymt.Pso.Load(ms);
            ymt.FileFormat = YmtFileFormat.PSO;
            ymt.ContentType = YmtFileContentType.DoorTuning;
            ymt.DoorTuning = this;
            ymt.Name = "doortuning.ymt";
            ymt.Loaded = true;
            return ymt;
        }

        public static YmtFile? LoadFromGame(RpfManager rpfMan)
        {
            if (rpfMan == null) return null;
            var ymt = rpfMan.GetFile<YmtFile>(GameRelativePath);
            if (ymt?.Pso == null) return ymt;
            ymt.ContentType = YmtFileContentType.DoorTuning;
            ymt.DoorTuning = FromYmt(ymt);
            return ymt;
        }

        private static string Escape(string s) =>
            System.Security.SecurityElement.Escape(s) ?? string.Empty;
    }

    public class DoorNamedTuning
    {
        public string Name { get; set; } = string.Empty;
        public DoorTuningParams Tuning { get; set; } = new();
    }

    public class DoorModelMapping
    {
        public string ModelName { get; set; } = string.Empty;
        public string TuningName { get; set; } = string.Empty;
    }

    public class DoorTuningParams
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
                    .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
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
            // rage__spdAABB often uses nested structure with x,y,z attributes on min/max nodes
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

            // Alternate AABB layout used by some PSO dumps
            var bbMin = box?.Element("BoxMin") ?? box?.Element("minExtent");
            var bbMax = box?.Element("BoxMax") ?? box?.Element("maxExtent");
            if (bbMin != null)
            {
                t.TriggerBoxMinX = AttrF(bbMin, "x");
                t.TriggerBoxMinY = AttrF(bbMin, "y");
                t.TriggerBoxMinZ = AttrF(bbMin, "z");
            }
            if (bbMax != null)
            {
                t.TriggerBoxMaxX = AttrF(bbMax, "x");
                t.TriggerBoxMaxY = AttrF(bbMax, "y");
                t.TriggerBoxMaxZ = AttrF(bbMax, "z");
            }

            t.BreakableByVehicle = ChildB(el, "BreakableByVehicle");
            t.BreakingImpulse = ChildF(el, "BreakingImpulse");
            t.ShouldLatchShut = ChildB(el, "ShouldLatchShut");
            t.MassMultiplier = ChildF(el, "MassMultiplier", 1f);
            t.WeaponImpulseMultiplier = ChildF(el, "WeaponImpulseMultiplier", 1f);
            t.RotationLimitAngle = ChildF(el, "RotationLimitAngle");
            t.TorqueAngularVelocityLimit = ChildF(el, "TorqueAngularVelocityLimit");
            t.StdDoorRotDir = el.Element("StdDoorRotDir")?.Value?.Trim() ?? "StdDoorOpenBothDir";
            if (string.IsNullOrEmpty(t.StdDoorRotDir)) t.StdDoorRotDir = "StdDoorOpenBothDir";

            return t;
        }

        public void WriteXml(StringBuilder sb, string indent)
        {
            string F(float v) => FloatUtil.ToString(v);
            sb.AppendLine($"{indent}<AutoOpenVolumeOffset x=\"{F(AutoOpenVolumeOffsetX)}\" y=\"{F(AutoOpenVolumeOffsetY)}\" z=\"{F(AutoOpenVolumeOffsetZ)}\" />");
            sb.AppendLine($"{indent}<Flags>{string.Join(" ", Flags)}</Flags>");
            sb.AppendLine($"{indent}<AutoOpenRadiusModifier value=\"{F(AutoOpenRadiusModifier)}\" />");
            sb.AppendLine($"{indent}<AutoOpenRate value=\"{F(AutoOpenRate)}\" />");
            sb.AppendLine($"{indent}<AutoOpenCosineAngleBetweenThreshold value=\"{F(AutoOpenCosineAngleBetweenThreshold)}\" />");
            sb.AppendLine($"{indent}<AutoOpenCloseRateTaper value=\"{AutoOpenCloseRateTaper.ToString().ToLowerInvariant()}\" />");
            sb.AppendLine($"{indent}<UseAutoOpenTriggerBox value=\"{UseAutoOpenTriggerBox.ToString().ToLowerInvariant()}\" />");
            sb.AppendLine($"{indent}<CustomTriggerBox value=\"{CustomTriggerBox.ToString().ToLowerInvariant()}\" />");
            sb.AppendLine($"{indent}<TriggerBoxMinMax type=\"rage__spdAABB\">");
            sb.AppendLine($"{indent} <min x=\"{F(TriggerBoxMinX)}\" y=\"{F(TriggerBoxMinY)}\" z=\"{F(TriggerBoxMinZ)}\" />");
            sb.AppendLine($"{indent} <max x=\"{F(TriggerBoxMaxX)}\" y=\"{F(TriggerBoxMaxY)}\" z=\"{F(TriggerBoxMaxZ)}\" />");
            sb.AppendLine($"{indent}</TriggerBoxMinMax>");
            sb.AppendLine($"{indent}<BreakableByVehicle value=\"{BreakableByVehicle.ToString().ToLowerInvariant()}\" />");
            sb.AppendLine($"{indent}<BreakingImpulse value=\"{F(BreakingImpulse)}\" />");
            sb.AppendLine($"{indent}<ShouldLatchShut value=\"{ShouldLatchShut.ToString().ToLowerInvariant()}\" />");
            sb.AppendLine($"{indent}<MassMultiplier value=\"{F(MassMultiplier)}\" />");
            sb.AppendLine($"{indent}<WeaponImpulseMultiplier value=\"{F(WeaponImpulseMultiplier)}\" />");
            sb.AppendLine($"{indent}<RotationLimitAngle value=\"{F(RotationLimitAngle)}\" />");
            sb.AppendLine($"{indent}<TorqueAngularVelocityLimit value=\"{F(TorqueAngularVelocityLimit)}\" />");
            sb.AppendLine($"{indent}<StdDoorRotDir>{StdDoorRotDir}</StdDoorRotDir>");
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
