// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Validation;

public sealed class XmlIndexFactProducer : IXmlIndexFactProducer
{
    public IReadOnlyList<XmlFact> Produce(string documentUri, GameIndex index)
    {
        if (!index.Documents.TryGetValue(documentUri, out var doc))
            return [];

        var facts = new List<XmlFact>();

        foreach (var sym in doc.Symbols)
        {
            if (sym.Origin is not FileOrigin fo)
                continue;
            if (!index.WorkspaceDefinitions.TryGetValue(sym.Id, out var all) || all.Length <= 1)
                continue;

            // Only a collision WITHIN the same project layer AND same TypeName is a duplicate.
            // Cross-layer definitions are valid overrides (surfaced as code lens, not diagnostic).
            // Cross-type definitions (same ID, different TypeName) are shadows, not duplicates —
            // the engine allows distinct types to share an ID; they are warned separately.
            var myRank = index.LayerRankOf(sym);
            var sameLayer = all.Where(s => index.LayerRankOf(s) == myRank).ToList();
            var sameTypeSameLayer = sameLayer
                .Where(s => string.Equals(s.TypeName, sym.TypeName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sameTypeSameLayer.Count > 1)
                facts.Add(new XmlSymbolFact(documentUri, fo.Line, fo.Column ?? 0, 0, sym.Id, sameTypeSameLayer));
        }

        foreach (var reference in doc.References)
        {
            // "enum:{EnumName}/{Value}" ids are synthetic markers recorded by
            // XmlGameDocumentParser.CollectEnumReferences for go-to-definition/rename. They can
            // never resolve against the object index, so producing a fact would make
            // UnresolvedReferenceHandler flag every dynamic-enum tag value regardless of
            // validity. Enum membership is validated by NamedEnumValueHandlerBase instead.
            if (reference.TargetId.StartsWith("enum:", StringComparison.Ordinal))
                continue;

            var resolved = index.Resolve(reference.TargetId, reference.ExpectedTypeName);
            facts.Add(new XmlReferenceFact(
                documentUri,
                reference.Line,
                reference.Column,
                reference.Length,
                reference.TargetId,
                resolved,
                reference.ExpectedTypeName));
        }

        return facts;
    }
}