using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Revit2GoldSrc.Core
{
    public static class MaterialImagePaths
    {
        public static string? Resolve(string bitmap, IEnumerable<string> searchFolders)
        {
            var roots = searchFolders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string part in bitmap.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string path = Environment.ExpandEnvironmentVariables(part.Trim().Trim('"'));
                if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile) path = uri.LocalPath;
                if (File.Exists(path)) return Path.GetFullPath(path);
                foreach (string root in roots)
                {
                    // Rooted missing paths may have moved with the model: search the configured folders by filename too.
                    foreach (string candidate in new[] { Path.Combine(root, path.TrimStart('\\', '/')), Path.Combine(root, Path.GetFileName(path)) })
                        if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
            }
            return null;
        }
    }
}
