using System;
using System.IO;
using System.Text.RegularExpressions;
using CodeWalker.GameFiles;

namespace CodeWalker.DoorEditor
{
    /// <summary>
    /// FiveM resource bundle: folder with doortuning.ymt (Meta XML),
    /// gta5.meta (replace_level_meta), and fxmanifest.lua.
    /// </summary>
    public static class FivemResourceExport
    {
        private static readonly Regex DoorTuningResourcePath = new(
            @"resources:/[^/\s<]+/doortuning",
            RegexOptions.CultureInvariant);

        public static string SanitizeResourceName(string raw)
        {
            var trimmed = (raw ?? string.Empty).Trim();
            trimmed = Regex.Replace(trimmed, @"^\[", "");
            trimmed = Regex.Replace(trimmed, @"\]$", "");
            trimmed = Regex.Replace(trimmed, @"[^a-zA-Z0-9_-]+", "_");
            trimmed = trimmed.Trim('_');
            return string.IsNullOrEmpty(trimmed) ? "doortuning" : trimmed;
        }

        public static string Gta5MetaForResource(string resourceName)
        {
            var name = SanitizeResourceName(resourceName);
            var templatePath = Path.Combine(AppContext.BaseDirectory, "FiveM", "gta5.meta");
            if (!File.Exists(templatePath))
                throw new FileNotFoundException("Missing FiveM template gta5.meta next to the app.", templatePath);

            var template = File.ReadAllText(templatePath);
            return DoorTuningResourcePath.Replace(template, $"resources:/{name}/doortuning");
        }

        public static string FxManifestTemplate()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "FiveM", "fxmanifest.lua");
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing FiveM template fxmanifest.lua next to the app.", path);

            var text = File.ReadAllText(path);
            return text.EndsWith('\n') ? text : text + Environment.NewLine;
        }

        /// <param name="resourcesParentFolder">e.g. server resources\[main]</param>
        /// <param name="resourceName">Folder name for the new resource</param>
        /// <returns>Full path to the created resource folder</returns>
        public static string ExportBundle(string resourcesParentFolder, string resourceName, DoorTuningDocument document)
        {
            if (string.IsNullOrWhiteSpace(resourcesParentFolder))
                throw new ArgumentException("Pick a resources folder.", nameof(resourcesParentFolder));
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            var name = SanitizeResourceName(resourceName);
            var dest = Path.Combine(resourcesParentFolder, name);
            Directory.CreateDirectory(dest);

            // Meta XML written as doortuning.ymt (not binary PSO).
            File.WriteAllText(Path.Combine(dest, "doortuning.ymt"), document.ToXml());
            File.WriteAllText(Path.Combine(dest, "gta5.meta"), Gta5MetaForResource(name));
            File.WriteAllText(Path.Combine(dest, "fxmanifest.lua"), FxManifestTemplate());

            return dest;
        }
    }
}
