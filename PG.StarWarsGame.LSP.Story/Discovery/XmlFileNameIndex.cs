// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Story.Discovery;

/// <summary>
///     Every XML file under the declared xml directories, by file name. Built once, on the first
///     miss of a scan, so a chain with many mis-filed references walks the trees a single time.
///     The engine itself never resolves a file by name; this answers "where is it, then" for the
///     author.
/// </summary>
public sealed class XmlFileNameIndex(IFileHelper fileHelper, IReadOnlyList<string> xmlRootsHighestFirst)
{
    private readonly Lazy<Dictionary<string, List<string>>>
        _byName = new(() => Build(fileHelper, xmlRootsHighestFirst));

    /// <summary>
    ///     Xml-directory-relative paths (<c>/</c> separators) of every file of that name, highest
    ///     layer first, each layer's paths in ordinal order.
    /// </summary>
    public IReadOnlyList<string> Find(string fileName)
    {
        return _byName.Value.TryGetValue(fileName, out var paths) ? paths : [];
    }

    private static Dictionary<string, List<string>> Build(IFileHelper fileHelper, IReadOnlyList<string> roots)
    {
        var fs = fileHelper.FileSystem;
        var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (!fs.Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try
            {
                files =
                [
                    .. fs.Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories)
                        .Order(StringComparer.Ordinal)
                ];
            }
            catch (IOException)
            {
                continue; // an unreadable root hides nothing a reader could act on
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                var rel = fs.Path.GetRelativePath(root, file).Replace('\\', '/');
                if (!byName.TryGetValue(fs.Path.GetFileName(file), out var list))
                    byName[fs.Path.GetFileName(file)] = list = [];
                list.Add(rel);
            }
        }

        return byName;
    }
}
