// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;
using HtmlAgilityPack;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Xml;

namespace PG.StarWarsGame.LSP.Xml.Validation;

public sealed class XmlLayerShadowFactProducer : IXmlFactProducer
{
    public string Name => "shadow";

    public bool AppliesTo(string documentUri)
    {
        return true;
    }

    public IReadOnlyList<XmlFact> Produce(string documentUri, ParsedXmlDocument document, GameIndex index)
    {
        if (!index.Documents.TryGetValue(documentUri, out var doc))
            return [];

        var declaredOverrides = ParseOverrideDeclarations(document.Html);
        var facts = new List<XmlFact>();

        var leafLayerRank = index.LeafLayerRank;
        var isLeafDoc = leafLayerRank > 0 && doc.LayerRank == leafLayerRank;

        foreach (var sym in doc.Symbols)
        {
            if (sym.Origin is not FileOrigin fo) continue;

            // Cross-layer shadow: leaf defines same (Id, TypeName) as a dep-layer workspace doc
            if (isLeafDoc && !declaredOverrides.Contains(sym.Id))
            {
                var pair = index.ResolveWithShadow(sym.Id, sym.TypeName);
                if (pair is { Shadowed: { } shadowed }
                    && shadowed.Origin is FileOrigin shadowedFo
                    && index.Documents.TryGetValue(shadowedFo.Uri, out var shadowedDoc)
                    && index.LayerRankOf(shadowed) < leafLayerRank)
                {
                    var layerName = shadowedDoc.LayerName ?? shadowedFo.Uri;
                    facts.Add(new XmlLayerShadowFact(documentUri, fo.Line, fo.Column ?? 0, 0, sym.Id, layerName));
                }
            }
        }

        return facts;
    }

    /// <summary>
    ///     Symbols the document declares it deliberately overrides, via
    ///     <c>&lt;!-- &lt;Override Name="X"/&gt; --&gt;</c>.
    ///     <para>
    ///         This is not suppression, and the two must not be conflated. It mirrors Java's
    ///         <c>@Override</c>: an assertion about the code, checked against it, which happens to
    ///         mean there is no problem left to report - hence no fact is produced at all. A
    ///         suppression makes no claim and hides a problem that does exist, which is why it is
    ///         applied far later, on the way out of the publisher.
    ///     </para>
    /// </summary>
    private static HashSet<string> ParseOverrideDeclarations(HtmlDocument doc)
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