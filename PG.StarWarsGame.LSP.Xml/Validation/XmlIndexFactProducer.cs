// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
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

            // Workspace-file symbols are keyed by file path/name for navigation only; the same
            // file across layers is a valid override (shadowed), never a duplicate to flag.
            if (sym.Kind == GameSymbolKind.WorkspaceFile)
                continue;

            // Story symbols repeat legally across threads and campaigns (event names are only
            // unique per thread; flags per campaign) - campaign-scoped duplicate detection lives
            // in the story graph diagnostics, not the index-wide check.
            if (StoryReferenceTypes.IsStorySymbolType(sym.TypeName))
                continue;

            // The generic pass also indexes every <Event> block as a StoryParser object. Story
            // campaigns are sandboxed per faction, so the same event name in another campaign's
            // thread is legal - same reasoning, same campaign-scoped diagnostics ownership.
            if (string.Equals(sym.TypeName, StoryReferenceTypes.ThreadFileTypeName,
                    StringComparison.OrdinalIgnoreCase))
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

            // Workspace-file references (plot manifests, story threads, Lua scripts) exist for
            // navigation and rename; their existence is validated by the campaign story chain, not
            // the index-wide reference check - so no unresolved-reference fact is produced.
            if (reference.ExpectedKind == GameSymbolKind.WorkspaceFile)
                continue;

            // Engine placeholders ("null"/"Default"/"None") are a valid "no object" value in any
            // reference position - never unresolved, never a type mismatch.
            if (EnginePlaceholders.IsPlaceholder(reference.TargetId))
                continue;

            // Story references exist for navigation and rename; their existence validation is
            // campaign-scoped (story graph diagnostics), not index-wide.
            if (StoryReferenceTypes.IsStorySymbolType(reference.ExpectedTypeName))
                continue;

            // An owner-agnostic reference names an ability that is indexed as {ownerId}$Name, so it
            // has to be matched across owners. The fact carries the bare name, not the marker, so
            // diagnostics read the way the value is actually written in the file.
            if (OwnerAgnosticReferenceId.TryGetBareName(reference.TargetId, out var bareName))
            {
                facts.Add(new XmlReferenceFact(
                    documentUri,
                    reference.Line,
                    reference.Column,
                    reference.Length,
                    bareName,
                    index.ResolveOwnerAgnostic(bareName),
                    reference.ExpectedTypeName));
                continue;
            }

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