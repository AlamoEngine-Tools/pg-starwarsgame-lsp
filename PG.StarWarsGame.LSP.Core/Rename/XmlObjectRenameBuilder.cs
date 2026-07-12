// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Core.Rename;

public static class XmlObjectRenameBuilder
{
    public static WorkspaceEdit? Build(
        string id, string newName, GameIndex index, ISchemaProvider schema,
        IDocumentTextSource textSource, ILogger logger)
    {
        if (!index.IsLeafOwned(id))
        {
            logger.LogDebug("Rename blocked: {Id} is not exclusively defined in the leaf layer", id);
            return null;
        }

        var changes = new Dictionary<DocumentUri, List<TextEdit>>();

        // XML definition edits — locate the name-attribute value in the definition file. Symbols
        // whose TypeName is not a schema object type (story events/flags/notifications) carry an
        // exact value column in their origin instead; the definition edit spans the id there.
        if (index.WorkspaceDefinitions.TryGetValue(id, out var defs))
            foreach (var sym in defs)
            {
                if (sym.Origin is not FileOrigin fo) continue;
                var nameTag = sym.TypeName is not null
                    ? schema.GetObjectType(sym.TypeName)?.NameTag
                    : null;
                if (nameTag is not null)
                {
                    var defRange = FindNameAttributeRange(fo.Uri, fo.Line, nameTag, id, textSource);
                    if (defRange is null) continue;
                    AddEdit(changes, fo.Uri, new TextEdit { NewText = newName, Range = defRange });
                }
                else if (fo.Column is { } column && StoryReferenceTypes.IsStorySymbolType(sym.TypeName))
                {
                    AddEdit(changes, fo.Uri, new TextEdit
                    {
                        NewText = newName,
                        Range = new LspRange(
                            new Position(fo.Line, column),
                            new Position(fo.Line, column + id.Length))
                    });
                }
            }

        // Reference edits — use precise positions from the index (covers both XML and Lua files).
        if (index.WorkspaceReferences.TryGetValue(id, out var refs))
            foreach (var r in refs)
                AddEdit(changes, r.DocumentUri, new TextEdit
                {
                    NewText = newName,
                    Range = new LspRange(
                        new Position(r.Line, r.Column),
                        new Position(r.Line, r.Column + r.Length))
                });

        if (changes.Count == 0)
            return null;

        logger.LogDebug("Rename XmlObject {Id} → {NewName}: {Count} file(s)", id, newName, changes.Count);
        return new WorkspaceEdit
        {
            Changes = changes.ToDictionary(kvp => kvp.Key, kvp => (IEnumerable<TextEdit>)kvp.Value)
        };
    }

    internal static LspRange? FindNameAttributeRange(
        string uri, int line, string nameTag, string currentValue, IDocumentTextSource textSource)
    {
        var text = textSource.GetText(uri)?.Text;
        if (text is null) return null;

        var lines = text.Split('\n');
        if (line >= lines.Length) return null;
        var lineText = lines[line].TrimEnd('\r');

        foreach (var quote in new[] { '"', '\'' })
        {
            var pattern = $"{nameTag}={quote}{currentValue}{quote}";
            var idx = lineText.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) continue;

            var valueStart = idx + nameTag.Length + 2; // +2 for '=' and opening quote
            return new LspRange(
                new Position(line, valueStart),
                new Position(line, valueStart + currentValue.Length));
        }

        return null;
    }

    private static void AddEdit(
        Dictionary<DocumentUri, List<TextEdit>> changes, string uri, TextEdit edit)
    {
        var key = DocumentUri.From(uri);
        if (!changes.TryGetValue(key, out var list))
            changes[key] = list = [];
        list.Add(edit);
    }
}