// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Core.Rename;

/// <summary>
///     Builds a <see cref="WorkspaceEdit" /> for renaming a dynamic-enum value (e.g. an Armor_Type or
///     GameObjectCategoryType entry), covering both its definition site and every referencing
///     occurrence. Separate from <see cref="XmlObjectRenameBuilder" /> because dynamic enum values are
///     tracked in <see cref="GameIndex.WorkspaceEnumValueDefinitions" />, a different shape than the
///     id-based <see cref="GameIndex.WorkspaceDefinitions" />/<see cref="GameIndex.WorkspaceReferences" />
///     model that builder relies on.
/// </summary>
public static class DynamicEnumValueRenameBuilder
{
    public static WorkspaceEdit? Build(
        string enumName, string valueName, string newName, GameIndex index,
        IDocumentTextSource textSource, ILogger logger)
    {
        if (!index.WorkspaceEnumValueDefinitions.TryGetValue(enumName, out var valueMap) ||
            !valueMap.TryGetValue(valueName, out var origin) || !origin.IsNavigable)
        {
            logger.LogDebug("Rename blocked: enum value {Enum}/{Value} has no navigable workspace definition",
                enumName, valueName);
            return null;
        }

        if (index.LayerRankOfUri(origin.Uri) != index.LeafLayerRank)
        {
            logger.LogDebug(
                "Rename blocked: enum value {Enum}/{Value} is defined in a dependency layer, not the leaf project",
                enumName, valueName);
            return null;
        }

        var changes = new Dictionary<DocumentUri, List<TextEdit>>();

        if (origin.Column is { } col)
        {
            // path$Element format (e.g. gameconstants.xml's <Armor_Types>...</Armor_Types> text
            // list) — the value is a plain text token at an exact (line, column).
            AddEdit(changes, origin.Uri, new TextEdit
            {
                NewText = newName,
                Range = new LspRange(new Position(origin.Line, col), new Position(origin.Line, col + valueName.Length))
            });
        }
        else
        {
            // Bare <EnumDefinition> format (e.g. gameobjectcategorytype.xml) — the value IS the XML
            // element name, appearing in both its opening and closing tag on this line. No column is
            // recorded for this format (see DynamicEnumExtractor.ParseEnumDefinitionFileWithLocations),
            // so the tag name is located by text search, same approach as
            // XmlObjectRenameBuilder.FindNameAttributeRange.
            var edits = FindElementNameEdits(origin.Uri, origin.Line, valueName, newName, textSource);
            if (edits.Count == 0)
            {
                logger.LogDebug("Rename blocked: could not locate element name '{Value}' at {Uri}:{Line}",
                    valueName, origin.Uri, origin.Line);
                return null;
            }

            foreach (var edit in edits)
                AddEdit(changes, origin.Uri, edit);
        }

        // Reference edits — every occurrence recorded as a GameReference with the synthetic
        // "enum:{EnumName}/{ValueName}" target id (see XmlGameDocumentParser.CollectEnumReferences).
        var id = $"enum:{enumName}/{valueName}";
        if (index.WorkspaceReferences.TryGetValue(id, out var refs))
            foreach (var r in refs)
                AddEdit(changes, r.DocumentUri, new TextEdit
                {
                    NewText = newName,
                    Range = new LspRange(new Position(r.Line, r.Column), new Position(r.Line, r.Column + r.Length))
                });

        if (changes.Count == 0)
            return null;

        logger.LogDebug("Rename enum value {Enum}/{Value} → {NewName}: {Count} file(s)",
            enumName, valueName, newName, changes.Count);
        return new WorkspaceEdit
        {
            Changes = changes.ToDictionary(kvp => kvp.Key, kvp => (IEnumerable<TextEdit>)kvp.Value)
        };
    }

    private static List<TextEdit> FindElementNameEdits(
        string uri, int line, string valueName, string newName, IDocumentTextSource textSource)
    {
        var text = textSource.GetText(uri)?.Text;
        if (text is null) return [];

        var lines = text.Split('\n');
        if (line >= lines.Length) return [];
        var lineText = lines[line].TrimEnd('\r');

        var edits = new List<TextEdit>();

        var openIdx = lineText.IndexOf($"<{valueName}", StringComparison.Ordinal);
        if (openIdx >= 0)
        {
            var start = openIdx + 1;
            edits.Add(new TextEdit
            {
                NewText = newName,
                Range = new LspRange(new Position(line, start), new Position(line, start + valueName.Length))
            });
        }

        var closeIdx = lineText.IndexOf($"</{valueName}>", StringComparison.Ordinal);
        if (closeIdx >= 0)
        {
            var start = closeIdx + 2;
            edits.Add(new TextEdit
            {
                NewText = newName,
                Range = new LspRange(new Position(line, start), new Position(line, start + valueName.Length))
            });
        }

        return edits;
    }

    private static void AddEdit(Dictionary<DocumentUri, List<TextEdit>> changes, string uri, TextEdit edit)
    {
        var key = DocumentUri.From(uri);
        if (!changes.TryGetValue(key, out var list))
            changes[key] = list = [];
        list.Add(edit);
    }
}
