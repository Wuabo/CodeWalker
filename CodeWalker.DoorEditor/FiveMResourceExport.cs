using System;
using System.IO;
using System.Text;

namespace CodeWalker.DoorEditor
{
    /// <summary>
    /// FiveM Meta XML doortuning .ymt only (no fxmanifest / gta5.meta).
    /// </summary>
    public static class FiveMResourceExport
    {
        public static void WriteYmt(string path, DoorTuningDocument document)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path is required.", nameof(path));
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, document.ToXml(), new UTF8Encoding(false));
        }

        /// <summary>
        /// Suggest a .ymt file name from an opened/source path (keeps basename when possible).
        /// </summary>
        public static string SuggestYmtFileName(string? sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                return "doortuning.ymt";

            var name = Path.GetFileName(sourcePath.Trim());
            if (string.IsNullOrEmpty(name))
                return "doortuning.ymt";

            if (name.EndsWith(".ymt.pso.xml", StringComparison.OrdinalIgnoreCase))
                return name[..^".pso.xml".Length]; // foo.ymt.pso.xml → foo.ymt
            if (name.EndsWith(".ymt.xml", StringComparison.OrdinalIgnoreCase))
                return name[..^".xml".Length]; // foo.ymt.xml → foo.ymt
            if (name.EndsWith(".ymt", StringComparison.OrdinalIgnoreCase))
                return name;
            if (name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                return Path.GetFileNameWithoutExtension(name) + ".ymt";

            return Path.GetFileNameWithoutExtension(name) + ".ymt";
        }
    }
}
