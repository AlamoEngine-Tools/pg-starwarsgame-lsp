// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Indexing;

/// <summary>
///     Which files the document parsers take, and which parser takes a file: XML under the declared
///     XML directories when the EaW XML context recognises it, everything else under the script roots
///     by extension.
/// </summary>
public sealed class ParseableFiles(
    IFileHelper fileHelper,
    IEnumerable<IGameDocumentParser> parsers,
    IEaWXmlContext eaWXmlContext)
{
    /// <summary>Every parseable file under <paramref name="xmlDirectories" /> and <paramref name="scriptRoots" />.</summary>
    public List<string> Collect(IEnumerable<string> xmlDirectories, IEnumerable<string> scriptRoots)
    {
        return
        [
            .. EnumerateFiles(xmlDirectories)
                .Where(f =>
                {
                    var ext = fileHelper.FileSystem.Path.GetExtension(f);
                    return ext.Equals(".xml", StringComparison.OrdinalIgnoreCase)
                           && parsers.Any(p => p.CanParse(ext))
                           && eaWXmlContext.IsEaWXmlFile(fileHelper.PathToFileUri(f));
                })
                .Concat(EnumerateFiles(scriptRoots)
                    .Where(f =>
                    {
                        var ext = fileHelper.FileSystem.Path.GetExtension(f);
                        return !ext.Equals(".xml", StringComparison.OrdinalIgnoreCase)
                               && parsers.Any(p => p.CanParse(ext));
                    }))
                .Distinct()
        ];
    }

    /// <summary>The parser that takes <paramref name="path" />, by its extension; null when none does.</summary>
    public IGameDocumentParser? ParserFor(string path)
    {
        var ext = fileHelper.FileSystem.Path.GetExtension(path);
        return parsers.FirstOrDefault(p => p.CanParse(ext));
    }

    private IEnumerable<string> EnumerateFiles(IEnumerable<string> roots)
    {
        return roots
            .Where(fileHelper.FileSystem.Directory.Exists)
            .SelectMany(folder =>
                fileHelper.FileSystem.Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories));
    }
}
