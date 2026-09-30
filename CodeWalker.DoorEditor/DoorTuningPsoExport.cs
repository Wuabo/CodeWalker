using System;
using System.IO;
using System.Text;
using System.Xml;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorEditor
{
    /// <summary>
    /// Game-safe doortuning.ymt writer.
    ///
    /// Core XmlPso/PsoBuilder (RPF Explorer Import XML → Save) does not 16-byte-align
    /// PSO data blocks or write a CHKS trailer — the game rejects those files.
    /// We use Core XmlPso to build schema/payload, then fix alignment + checksum here.
    /// </summary>
    public static class DoorTuningPsoExport
    {
        private const byte PadByte = 0x70;
        private const uint AabbWSentinel = 0xFA83126D;

        /// <summary>CodeWalker RPF Explorer Import XML shape (*.ymt.pso.xml).</summary>
        public static string ToCodeWalkerPsoXml(DoorTuningDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<CDoorTuningFile>");
            sb.AppendLine(" <NamedTuningArray itemType=\"CDoorTuningFile__NamedTuning\">");
            foreach (var nt in document.NamedTunings)
            {
                sb.AppendLine("  <Item>");
                sb.AppendLine($"   <Name>{Escape(nt.Name)}</Name>");
                sb.AppendLine("   <Tuning>");
                WriteTuning(sb, nt.Tuning ?? new DoorTuningParams(), "    ");
                sb.AppendLine("   </Tuning>");
                sb.AppendLine("  </Item>");
            }
            sb.AppendLine(" </NamedTuningArray>");
            sb.AppendLine(" <ModelToTuneMapping itemType=\"CDoorTuningFile__ModelToTuneName\">");
            foreach (var map in document.ModelMappings)
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

        public static byte[] SaveBinary(DoorTuningDocument document)
        {
            var xml = ToCodeWalkerPsoXml(document);
            var xdoc = new XmlDocument();
            xdoc.LoadXml(xml);
            var pso = XmlPso.GetPso(xdoc)
                ?? throw new InvalidDataException("XmlPso returned null for doortuning.");

            RealignDataBlocks(pso);
            PatchAabbPadding(pso);
            return WriteWithChecksum(pso);
        }

        private static void WriteTuning(StringBuilder sb, DoorTuningParams t, string indent)
        {
            string F(float v) => FloatUtil.ToString(v);
            var flags = t.Flags ?? [];
            sb.AppendLine($"{indent}<AutoOpenVolumeOffset x=\"{F(t.AutoOpenVolumeOffsetX)}\" y=\"{F(t.AutoOpenVolumeOffsetY)}\" z=\"{F(t.AutoOpenVolumeOffsetZ)}\" />");
            if (flags.Count == 0)
                sb.AppendLine($"{indent}<Flags />");
            else
                sb.AppendLine($"{indent}<Flags>{string.Join(" ", flags)}</Flags>");
            sb.AppendLine($"{indent}<AutoOpenRadiusModifier value=\"{F(t.AutoOpenRadiusModifier)}\" />");
            sb.AppendLine($"{indent}<AutoOpenRate value=\"{F(t.AutoOpenRate)}\" />");
            sb.AppendLine($"{indent}<AutoOpenCosineAngleBetweenThreshold value=\"{F(t.AutoOpenCosineAngleBetweenThreshold)}\" />");
            sb.AppendLine($"{indent}<AutoOpenCloseRateTaper value=\"{t.AutoOpenCloseRateTaper.ToString().ToLowerInvariant()}\" />");
            sb.AppendLine($"{indent}<UseAutoOpenTriggerBox value=\"{t.UseAutoOpenTriggerBox.ToString().ToLowerInvariant()}\" />");
            sb.AppendLine($"{indent}<CustomTriggerBox value=\"{t.CustomTriggerBox.ToString().ToLowerInvariant()}\" />");
            sb.AppendLine($"{indent}<TriggerBoxMinMax>");
            sb.AppendLine($"{indent} <min x=\"{F(t.TriggerBoxMinX)}\" y=\"{F(t.TriggerBoxMinY)}\" z=\"{F(t.TriggerBoxMinZ)}\" />");
            sb.AppendLine($"{indent} <max x=\"{F(t.TriggerBoxMaxX)}\" y=\"{F(t.TriggerBoxMaxY)}\" z=\"{F(t.TriggerBoxMaxZ)}\" />");
            sb.AppendLine($"{indent}</TriggerBoxMinMax>");
            sb.AppendLine($"{indent}<BreakableByVehicle value=\"{t.BreakableByVehicle.ToString().ToLowerInvariant()}\" />");
            sb.AppendLine($"{indent}<BreakingImpulse value=\"{F(t.BreakingImpulse)}\" />");
            sb.AppendLine($"{indent}<ShouldLatchShut value=\"{t.ShouldLatchShut.ToString().ToLowerInvariant()}\" />");
            sb.AppendLine($"{indent}<MassMultiplier value=\"{F(t.MassMultiplier)}\" />");
            sb.AppendLine($"{indent}<WeaponImpulseMultiplier value=\"{F(t.WeaponImpulseMultiplier)}\" />");
            sb.AppendLine($"{indent}<RotationLimitAngle value=\"{F(t.RotationLimitAngle)}\" />");
            sb.AppendLine($"{indent}<TorqueAngularVelocityLimit value=\"{F(t.TorqueAngularVelocityLimit)}\" />");
            var rot = string.IsNullOrWhiteSpace(t.StdDoorRotDir) ? "StdDoorOpenBothDir" : t.StdDoorRotDir;
            sb.AppendLine($"{indent}<StdDoorRotDir>{Escape(rot)}</StdDoorRotDir>");
        }

        private static void RealignDataBlocks(PsoFile pso)
        {
            var map = pso.DataMapSection ?? throw new InvalidDataException("PSO missing DataMap.");
            var dataSec = pso.DataSection ?? throw new InvalidDataException("PSO missing Data.");
            var old = dataSec.Data ?? throw new InvalidDataException("PSO data buffer empty.");
            var entries = map.Entries ?? throw new InvalidDataException("PSO DataMap empty.");
            if (entries.Length == 0) return;

            int totlen = 16;
            for (int i = 0; i < entries.Length; i++)
            {
                totlen += entries[i].Length;
                if (i < entries.Length - 1)
                    totlen = (totlen + 15) & ~15;
            }

            var next = new byte[totlen];
            for (int i = 8; i < 16; i++) next[i] = PadByte;
            if (old.Length >= 8)
                Buffer.BlockCopy(old, 0, next, 0, 8);

            int offset = 16;
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                if (e.Offset < 0 || e.Length < 0 || e.Offset + e.Length > old.Length)
                    throw new InvalidDataException($"Invalid PSO block map entry [{i}].");

                Buffer.BlockCopy(old, e.Offset, next, offset, e.Length);
                e.Offset = offset;
                offset += e.Length;

                if (i < entries.Length - 1)
                {
                    int aligned = (offset + 15) & ~15;
                    while (offset < aligned)
                        next[offset++] = PadByte;
                }
            }

            dataSec.Data = next;
        }

        /// <summary>
        /// NamedTuning is 144 bytes; TriggerBox Float4a.w uses vanilla sentinel 0xFA83126D when xyz ≠ 0.
        /// </summary>
        private static void PatchAabbPadding(PsoFile pso)
        {
            const uint NamedTuningHash = 0x243B5E8B; // CDoorTuningFile__NamedTuning
            const int ItemSize = 144;
            var map = pso.DataMapSection?.Entries;
            var data = pso.DataSection?.Data;
            if (map == null || data == null) return;

            foreach (var e in map)
            {
                if ((uint)e.NameHash != NamedTuningHash) continue;
                if (e.Length < ItemSize) continue;
                int count = e.Length / ItemSize;
                for (int i = 0; i < count; i++)
                {
                    int baseOff = e.Offset + i * ItemSize;
                    PatchAabbW(data, baseOff + 80);
                    PatchAabbW(data, baseOff + 96);
                }
            }
        }

        private static void PatchAabbW(byte[] data, int float4Offset)
        {
            if (float4Offset + 16 > data.Length) return;
            bool nonzero =
                ReadU32BE(data, float4Offset) != 0 ||
                ReadU32BE(data, float4Offset + 4) != 0 ||
                ReadU32BE(data, float4Offset + 8) != 0;
            WriteU32BE(data, float4Offset + 12, nonzero ? AabbWSentinel : 0u);
        }

        private static byte[] WriteWithChecksum(PsoFile pso)
        {
            pso.CHKSSection = new PsoCHKSSection
            {
                FileSize = 0,
                Checksum = 0,
                Unk0 = 0x79707070
            };

            using var ms = new MemoryStream();
            var writer = new DataWriter(ms, Endianess.BigEndian);
            pso.DataSection?.Write(writer);
            pso.DataMapSection?.Write(writer);
            pso.SchemaSection?.Write(writer);
            pso.STRFSection?.Write(writer);
            pso.STRSSection?.Write(writer);
            pso.STRESection?.Write(writer);
            pso.PSIGSection?.Write(writer);
            pso.CHKSSection.Write(writer);

            var bytes = ms.ToArray();
            if (bytes.Length < 20)
                return bytes;

            uint fileSize = (uint)bytes.Length;
            uint checksum = ComputePsoChecksum(bytes);
            WriteU32BE(bytes, bytes.Length - 12, fileSize);
            WriteU32BE(bytes, bytes.Length - 8, checksum);
            pso.CHKSSection.FileSize = fileSize;
            pso.CHKSSection.Checksum = checksum;
            return bytes;
        }

        /// <summary>Rockstar PSO CHKS joaat (seed 0x3FAC7125, signed bytes).</summary>
        public static uint ComputePsoChecksum(byte[] memory)
        {
            uint hash = 0x3FAC7125;
            for (int i = 0; i < memory.Length; i++)
            {
                hash += (uint)(sbyte)memory[i];
                hash += hash << 10;
                hash ^= hash >> 6;
            }
            hash += hash << 3;
            hash ^= hash >> 11;
            hash += hash << 15;
            return hash;
        }

        private static uint ReadU32BE(byte[] data, int offset) =>
            ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
            ((uint)data[offset + 2] << 8) | data[offset + 3];

        private static void WriteU32BE(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }

        private static string Escape(string s) =>
            System.Security.SecurityElement.Escape(s ?? string.Empty) ?? string.Empty;
    }
}
