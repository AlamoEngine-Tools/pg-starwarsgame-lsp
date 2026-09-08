// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Core.Rename;

/// <summary>
///     Renames a workspace file (story plot manifest / thread / Lua script) referenced through a
///     <see cref="Schema.ReferenceKind.WorkspaceFile" /> tag: a <c>RenameFile</c> resource operation
///     for the file on disk, plus a text edit rewriting the <em>stem</em> (base name without
///     extension) of every reference to it. Each reference keeps its own directory prefix and
///     extension text, so <c>Conquests\X.xml</c>, <c>X.XML</c> and a bare Lua name all update
///     consistently while staying in the form the author wrote them.
/// </summary>
public static class FileReferenceRenameBuilder
{
    public static WorkspaceEdit? Build(string id, string newStem, GameIndex index,
        IDocumentTextSource textSource, ILogger logger)
    {
        // A rename changes only the base name: a directory separator would move the file, and any
        // other character invalid in a file name would produce a file that cannot be created.
        if (string.IsNullOrWhiteSpace(newStem) || !IsValidFileStem(newStem))
            return null;
        if (!index.IsLeafOwned(id))
        {
            logger.LogDebug("File rename blocked: {Id} is not owned by the leaf layer", id);
            return null;
        }

        if (index.Resolve(id) is not { Kind: GameSymbolKind.WorkspaceFile } symbol ||
            symbol.Origin is not FileOrigin { IsNavigable: true } fo)
            return null;

        var isLua = WorkspaceFileKey.HasType(id, WorkspaceFileKey.LuaScriptType);

        var (dir, _, ext) = SplitPath(fo.Uri);
        var newUri = dir + newStem + ext;
        if (string.Equals(newUri, fo.Uri, StringComparison.Ordinal)) return null;

        var changes = new List<WorkspaceEditDocumentChange>
        {
            new(new RenameFile
            {
                OldUri = DocumentUri.From(fo.Uri),
                NewUri = DocumentUri.From(newUri),
                Options = new RenameFileOptions { Overwrite = false, IgnoreIfExists = false }
            })
        };

        var edits = new Dictionary<string, List<TextEdit>>(StringComparer.Ordinal);
        if (index.WorkspaceReferences.TryGetValue(id, out var refs))
            foreach (var r in refs)
            {
                var range = StemRange(r.DocumentUri, r.Line, r.Column, r.Length, isLua, textSource);
                if (range is null) continue;
                if (!edits.TryGetValue(r.DocumentUri, out var list))
                    edits[r.DocumentUri] = list = [];
                list.Add(new TextEdit { NewText = newStem, Range = range });
            }

        foreach (var (uri, list) in edits)
            changes.Add(new WorkspaceEditDocumentChange(new TextDocumentEdit
            {
                TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = DocumentUri.From(uri) },
                Edits = new TextEditContainer(list)
            }));

        logger.LogDebug("File rename {Id} -> {NewStem}: file + {Files} referencing file(s)",
            id, newStem, edits.Count);
        return new WorkspaceEdit { DocumentChanges = new Container<WorkspaceEditDocumentChange>(changes) };
    }

    /// <summary>
    ///     The stem sub-range of a reference token: everything up to and including the last
    ///     directory separator, and (for XML files) the extension, are left untouched. Returns null
    ///     when the token cannot be located in the current text.
    /// </summary>
    public static LspRange? StemRange(string documentUri, int line, int column, int length,
        bool isLua, IDocumentTextSource textSource)
    {
        var text = textSource.GetText(documentUri)?.Text;
        if (text is null) return null;

        var lines = text.Split('\n');
        if (line >= lines.Length) return null;
        var lineText = lines[line].TrimEnd('\r');
        if (column < 0 || column + length > lineText.Length) return null;

        var token = lineText.Substring(column, length);
        var baseStart = token.LastIndexOfAny(['/', '\\']) + 1; // 0 when there is no directory
        var basename = token[baseStart..];
        var dot = basename.LastIndexOf('.');
        var stemLength = isLua || dot <= 0 ? basename.Length : dot;

        var startCol = column + baseStart;
        return new LspRange(new Position(line, startCol), new Position(line, startCol + stemLength));
    }

    // The characters no major file system accepts in a name, folded together so the guard is the
    // same on every platform: the Windows-reserved set plus the POSIX separator. '\' and '/' are
    // both included so a stem never silently turns into a path.
    private static readonly char[] InvalidStemChars = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    private static bool IsValidFileStem(string stem)
    {
        foreach (var c in stem)
            if (c < ' ' || Array.IndexOf(InvalidStemChars, c) >= 0)
                return false;

        // Windows silently strips a trailing dot or space, which would desync the requested name
        // from the file actually created; reject rather than surprise the author.
        var last = stem[^1];
        return last is not ('.' or ' ');
    }

    private static (string Dir, string Stem, string Ext) SplitPath(string uri)
    {
        var slash = uri.LastIndexOf('/');
        var dir = slash >= 0 ? uri[..(slash + 1)] : "";
        var name = slash >= 0 ? uri[(slash + 1)..] : uri;
        var dot = name.LastIndexOf('.');
        return dot > 0 ? (dir, name[..dot], name[dot..]) : (dir, name, "");
    }
}
