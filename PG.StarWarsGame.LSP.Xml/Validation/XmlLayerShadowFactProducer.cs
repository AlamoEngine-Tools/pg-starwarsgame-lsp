// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.Validation;

public sealed class XmlLayerShadowFactProducer : IXmlLayerShadowFactProducer
{
    public IReadOnlyList<XmlFact> Produce(string documentUri, ParsedXmlDocument document, GameIndex index)
    {
        if (!index.Documents.TryGetValue(documentUri, out var doc))
            return [];

        var suppressed = ParseSuppressionComments(document.Html);
        var facts = new List<XmlFact>();

        var leafLayerRank = index.LeafLayerRank;
        var isLeafDoc = leafLayerRank > 0 && doc.LayerRank == leafLayerRank;

        foreach (var sym in doc.Symbols)
        {
            if (sym.Origin is not FileOrigin fo) continue;

            // Cross-layer shadow: leaf defines same (Id, TypeName) as a dep-layer workspace doc
            if (isLeafDoc && !suppressed.Contains(sym.Id))
            {
                var pair = index.ResolveWithShadow(sym.Id);
                if (pair is { Shadowed: { } shadowed }
                    && shadowed.Origin is FileOrigin shadowedFo
                    && index.Documents.TryGetValue(shadowedFo.Uri, out var shadowedDoc)
                    && index.LayerRankOf(shadowed) < leafLayerRank)
                {
                    var layerName = shadowedDoc.LayerName ?? shadowedFo.Uri;
                    facts.Add(new XmlLayerShadowFact(documentUri, fo.Line, fo.Column ?? 0, 0, sym.Id, layerName));
                }
            }

            // Cross-type shadow: same Id defined with a different TypeName anywhere in the workspace.
            // Story symbols are exempt from both sides: every story event is indexed twice by design
            // (generic StoryParser object symbol + StoryEvent symbol for the same element), and
            // SET_FLAG names double as Lua globals — mirrors the duplicate/unresolved exemptions
            // in XmlIndexFactProducer.
            if (sym.TypeName is null) continue;
            if (StoryReferenceTypes.IsStorySymbolType(sym.TypeName)) continue;

            // Only emit from the highest-rank definition of each (Id, TypeName) pair to avoid
            // double-warnings when both sides of the collision are indexed workspace documents
            if (!index.WorkspaceDefinitions.TryGetValue(sym.Id, out var allDefs)) continue;
            var maxRankForThisType = allDefs
                .Where(s => string.Equals(s.TypeName, sym.TypeName, StringComparison.OrdinalIgnoreCase))
                .Select(index.LayerRankOf)
                .DefaultIfEmpty(0)
                .Max();
            if (index.LayerRankOf(sym) < maxRankForThisType) continue;

            var collidingTypes = allDefs
                .Where(s => !string.Equals(s.TypeName, sym.TypeName, StringComparison.OrdinalIgnoreCase)
                            && s.TypeName is not null
                            && !StoryReferenceTypes.IsStorySymbolType(s.TypeName))
                .Select(s => s.TypeName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (collidingTypes.Count == 0 || suppressed.Contains(sym.Id)) continue;

            foreach (var colliding in collidingTypes)
                facts.Add(new XmlCrossTypeShadowFact(
                    documentUri, fo.Line, fo.Column ?? 0, 0, sym.Id, sym.TypeName, colliding));
        }

        return facts;
    }

    private static HashSet<string> ParseSuppressionComments(HtmlDocument doc)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in doc.DocumentNode.DescendantsAndSelf())
        {
            if (node.NodeType != HtmlNodeType.Comment) continue;
            var m = Regex.Match(node.OuterHtml,
                @"<Override\s+Name\s*=\s*[""']?([^""'\s/>]+)[""']?\s*/?>",
                RegexOptions.IgnoreCase);
            if (m.Success)
                ids.Add(m.Groups[1].Value);
        }

        return ids;
    }
}