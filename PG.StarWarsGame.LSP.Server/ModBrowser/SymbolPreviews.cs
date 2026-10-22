// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Server.ModBrowser;

/// <summary>
///     An entry's preview (#270): the tiles its type's adapter lists, in the order the grid places
///     them, each sized in cells. The adapters live here rather than in the webview, so each is
///     tested against the index; the webview renders tile kinds and owns only the layout.
/// </summary>
/// <remarks>
///     A tile with nothing to show is left out of the list rather than drawn empty: a clean entry
///     has no Problems tile, an object only the game defines has no Definition.
/// </remarks>
public sealed class SymbolPreviews(
    GameIndex index,
    ISchemaProvider schema,
    IVariantTagSource tags,
    SymbolRelations relations,
    IDocumentTextSource texts,
    IXmlDiagnosticsCollector diagnostics,
    ToolsFeatureFlags tools)
{
    private const string GameObjects = "xml:GameObjectType";
    private const string ReferencedBy = "referencedBy";

    /// <summary>How many entries a relation's tile lists; the rest is one click away in the rail.</summary>
    private const int ListedEntries = 20;

    private static readonly GetSymbolPreviewResult s_none = new(null, []);

    public GetSymbolPreviewResult Preview(string page, string id)
    {
        // Assets, scripts and the schema get their adapters later; a click on them only selects.
        if (!page.StartsWith("xml:", StringComparison.Ordinal)) return s_none;
        var node = relations.Node(page, id);
        if (node.Entry is not { } entry) return s_none;

        var gameObject = page == GameObjects;
        var element = ElementOf(node.Location);
        var tiles = new List<PreviewTile>
        {
            // Two rows: the name, its tags and up to three key/value lines do not fit one.
            new("identity", 2, 2) { Entry = entry, Location = node.Location, VariantOf = node.VariantOf }
        };
        if (element is not null && Problems(element) is { } problems) tiles.Add(problems);
        if (gameObject)
        {
            if (Encyclopedia(entry.Id) is { } encyclopedia) tiles.Add(encyclopedia);
        }
        else
        {
            // An object of another type shows what its own tags name; a game object's are in the rail.
            tiles.AddRange(node.Relations
                .Where(r => r.Relation is not (ReferencedBy or "definitions") && r.Count > 0)
                .Select(r => Entries(page, entry.Id, r.Relation)));
        }

        if (element is not null) tiles.Add(Definition(element, entry.Id, node.VariantOf));
        tiles.Add(Entries(page, entry.Id, ReferencedBy));
        return new GetSymbolPreviewResult(gameObject ? "gameObject" : "xmlObject", tiles);
    }

    /// <summary>The definition the game uses, as its element in its file; null for one no file holds.</summary>
    private Element? ElementOf(SymbolLocation? location)
    {
        if (location is null || texts.GetText(location.Uri) is not { } document) return null;
        var parsed = ParsedXmlDocument.Parse(document.Text);
        var span =
            parsed.Spans.Elements.FirstOrDefault(e => parsed.LineIndex.GetPosition(e.Start).Line == location.Line);
        if (span is null) return null;
        return new Element(location.Uri, document.Text, document.Text[span.Start..span.End], location.Line,
            parsed.LineIndex.GetPosition(span.End - 1).Line);
    }

    // The errors and warnings inside the element; a hint is not a problem.
    private PreviewTile? Problems(Element element)
    {
        var found = diagnostics.Collect(element.Uri, element.Document, index)
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Where(d => d.Range.Start.Line >= element.StartLine && d.Range.Start.Line <= element.EndLine)
            .Select(d => new PreviewEntry(d.Message, d.Code?.String)
            {
                Location = new SymbolLocation(element.Uri, d.Range.Start.Line)
            })
            .ToList();
        return found.Count == 0 ? null : new PreviewTile("problems", 1, 1) { Count = found.Count, Entries = found };
    }

    // The card is drawn at the game's own width, scaled into two cells; one would make it unreadable.
    private PreviewTile? Encyclopedia(string id)
    {
        return tools.Encyclopedia && index.Resolve(id) is { } symbol &&
               EncyclopediaTags.DefinesBody(index, tags, symbol)
            ? new PreviewTile("encyclopedia", 2, 3) { ObjectId = id }
            : null;
    }

    private PreviewTile Definition(Element element, string id, string? variantOf)
    {
        string? effective = null;
        if (tools.Variants && !string.IsNullOrEmpty(variantOf))
        {
            var resolved = new EffectiveObjectResolver(index, schema, tags).Resolve(id);
            if (resolved.Found) effective = EffectiveObjectXmlRenderer.Render(resolved);
        }

        return new PreviewTile("definition", 2, 2) { Text = element.Text, Effective = effective };
    }

    private PreviewTile Entries(string page, string id, string relation)
    {
        var rows = relations.Rows(page, id, relation) ?? [];
        return new PreviewTile("entries", 1, 2)
        {
            Relation = relation,
            Count = rows.Count,
            Entries = rows.Take(ListedEntries).Select(r => new PreviewEntry(r.Name, r.Type) { Row = r }).ToList()
        };
    }

    /// <param name="Document">The whole file, which the diagnostics are collected over.</param>
    /// <param name="Text">The element alone, as written.</param>
    private sealed record Element(string Uri, string Document, string Text, int StartLine, int EndLine);
}