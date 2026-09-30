using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace CodeWalker.DoorEditor
{
    /// <summary>
    /// Writes a FiveM replace_level_meta resource folder:
    /// doortuning.ymt (Meta XML), fxmanifest.lua, gta5.meta — no companion .xml.
    /// </summary>
    public static class FiveMResourceExport
    {
        public static void WriteResourceFolder(string folder, DoorTuningDocument document, string? resourceName = null)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("Folder is required.", nameof(folder));
            Directory.CreateDirectory(folder);

            var name = string.IsNullOrWhiteSpace(resourceName)
                ? Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : resourceName.Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = "wuabo_meta";

            File.WriteAllText(Path.Combine(folder, "doortuning.ymt"), document.ToXml(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "fxmanifest.lua"), BuildFxManifest(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "gta5.meta"), BuildGta5Meta(name), new UTF8Encoding(false));
        }

        private static string BuildFxManifest() =>
            """
            fx_version 'cerulean'
            game 'gta5'

            replace_level_meta 'gta5'

            files {
                'gta5.meta',
                'doortuning.ymt'
            }
            """;

        private static string BuildGta5Meta(string resourceName)
        {
            var template = LoadGta5Template();
            var doorPath = $"resources:/{resourceName}/doortuning";

            // Replace any resources:/…/doortuning filename with this resource.
            const string marker = "resources:/";
            var start = template.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            while (start >= 0)
            {
                var end = template.IndexOf("</filename>", start, StringComparison.OrdinalIgnoreCase);
                if (end < 0) break;
                var path = template.Substring(start, end - start);
                if (path.Contains("doortuning", StringComparison.OrdinalIgnoreCase))
                    return template.Substring(0, start) + doorPath + template.Substring(end);
                start = template.IndexOf(marker, end, StringComparison.OrdinalIgnoreCase);
            }

            return template;
        }

        private static string LoadGta5Template()
        {
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "Templates", "gta5.meta"),
                Path.Combine(baseDir, "gta5.meta"),
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? baseDir, "Templates", "gta5.meta"),
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c))
                    return File.ReadAllText(c);
            }

            throw new FileNotFoundException(
                "FiveM export template Templates/gta5.meta was not found next to the app.");
        }
    }
}
