// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

public sealed class XmlIndexFactProducerTest
{
    private static readonly XmlIndexFactProducer Sut = new();

    private static GameSymbol MakeSym(string id, string uri, int line, string typeName = "SpaceUnit")
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new FileOrigin(uri, line, 0), null);
    }

    private static GameIndex BuildIndex(
        IEnumerable<GameSymbol> symbols,
        IEnumerable<GameReference> references,
        string documentUri = "file:///a.xml")
    {
        var allSymbols = symbols.ToList();
        var allRefs = references.ToList();

        var doc = new DocumentIndex(documentUri, 1,
            allSymbols.ToImmutableArray(),
            allRefs.ToImmutableArray());

        var workspaceDefs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty;
        foreach (var sym in allSymbols)
            workspaceDefs = workspaceDefs.TryGetValue(sym.Id, out var arr)
                ? workspaceDefs.SetItem(sym.Id, arr.Add(sym))
                : workspaceDefs.Add(sym.Id, ImmutableArray.Create(sym));

        var workspaceRefs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty;
        foreach (var r in allRefs)
            workspaceRefs = workspaceRefs.TryGetValue(r.TargetId, out var arr)
                ? workspaceRefs.SetItem(r.TargetId, arr.Add(r))
                : workspaceRefs.Add(r.TargetId, ImmutableArray.Create(r));

        return new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(documentUri, doc),
            workspaceDefs,
            workspaceRefs);
    }

    [Fact]
    public void No_documents_produces_no_facts()
    {
        var facts = Sut.Produce("file:///missing.xml", GameIndex.Empty);
        Assert.Empty(facts);
    }

    [Fact]
    public void Single_symbol_no_duplicate_produces_no_symbol_fact()
    {
        var sym = MakeSym("X1", "file:///a.xml", 0);
        var index = BuildIndex([sym], []);
        var facts = Sut.Produce("file:///a.xml", index);
        Assert.DoesNotContain(facts, f => f is XmlSymbolFact);
    }

    [Fact]
    public void Duplicate_symbol_in_same_document_emits_symbol_fact()
    {
        var sym1 = MakeSym("X1", "file:///a.xml", 0);
        var sym2 = MakeSym("X1", "file:///a.xml", 5);

        var doc = new DocumentIndex("file:///a.xml", 1,
            ImmutableArray.Create(sym1, sym2),
            ImmutableArray<GameReference>.Empty);

        var workspaceDefs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("X1", ImmutableArray.Create(sym1, sym2));

        var index = new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add("file:///a.xml", doc),
            workspaceDefs,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var facts = Sut.Produce("file:///a.xml", index).OfType<XmlSymbolFact>().ToList();
        Assert.Equal(2, facts.Count);
        Assert.All(facts, f => Assert.Equal("X1", f.SymbolId));
        Assert.Equal(2, facts[0].AllDefinitions.Count);
    }

    [Fact]
    public void CrossLayerOverride_DoesNotEmitDuplicateFact()
    {
        // A same-id definition in a different project layer is a valid override (surfaced as a code
        // lens), not a duplicate — so no XmlSymbolFact is produced for it.
        var core = MakeSym("UNIT_A", "file:///core/u.xml", 0);
        var rev = MakeSym("UNIT_A", "file:///rev/u.xml", 0);

        var coreDoc = new DocumentIndex("file:///core/u.xml", 1,
            ImmutableArray.Create(core), ImmutableArray<GameReference>.Empty, LayerRank: 0, LayerName: "Core");
        var revDoc = new DocumentIndex("file:///rev/u.xml", 1,
            ImmutableArray.Create(rev), ImmutableArray<GameReference>.Empty, LayerRank: 1, LayerName: "Rev");

        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_A", ImmutableArray.Create(core, rev));

        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add("file:///core/u.xml", coreDoc)
                .Add("file:///rev/u.xml", revDoc),
            defs,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        Assert.DoesNotContain(Sut.Produce("file:///rev/u.xml", index), f => f is XmlSymbolFact);
        Assert.DoesNotContain(Sut.Produce("file:///core/u.xml", index), f => f is XmlSymbolFact);
    }

    [Fact]
    public void SameLayerCollision_InDifferentFiles_StillEmitsDuplicateFact()
    {
        var a = MakeSym("UNIT_A", "file:///rev/a.xml", 0);
        var b = MakeSym("UNIT_A", "file:///rev/b.xml", 0);

        var aDoc = new DocumentIndex("file:///rev/a.xml", 1,
            ImmutableArray.Create(a), ImmutableArray<GameReference>.Empty, LayerRank: 1, LayerName: "Rev");
        var bDoc = new DocumentIndex("file:///rev/b.xml", 1,
            ImmutableArray.Create(b), ImmutableArray<GameReference>.Empty, LayerRank: 1, LayerName: "Rev");

        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_A", ImmutableArray.Create(a, b));

        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add("file:///rev/a.xml", aDoc)
                .Add("file:///rev/b.xml", bDoc),
            defs,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var facts = Sut.Produce("file:///rev/a.xml", index).OfType<XmlSymbolFact>().ToList();
        var f = Assert.Single(facts);
        Assert.Equal(2, f.AllDefinitions.Count);
    }

    [Fact]
    public void Resolved_reference_emits_reference_fact_with_resolved_symbol()
    {
        var target = MakeSym("Target1", "file:///b.xml", 0);
        var targetIndex = new DocumentIndex("file:///b.xml", 1,
            ImmutableArray.Create(target), ImmutableArray<GameReference>.Empty);

        var reference = new GameReference("Target1", GameSymbolKind.XmlObject, "SpaceUnit",
            "file:///a.xml", 2, 0, 7);

        var doc = new DocumentIndex("file:///a.xml", 1,
            ImmutableArray<GameSymbol>.Empty,
            ImmutableArray.Create(reference));

        var workspaceDefs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("Target1", ImmutableArray.Create(target));

        var workspaceRefs = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
            .Add("Target1", ImmutableArray.Create(reference));

        var index = new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add("file:///a.xml", doc)
                .Add("file:///b.xml", targetIndex),
            workspaceDefs,
            workspaceRefs);

        var facts = Sut.Produce("file:///a.xml", index).OfType<XmlReferenceFact>().ToList();
        var f = Assert.Single(facts);
        Assert.Equal("Target1", f.TargetId);
        Assert.NotNull(f.Resolved);
        Assert.Equal("SpaceUnit", f.ExpectedTypeName);
        Assert.Equal("file:///a.xml", f.DocumentUri);
    }

    [Fact]
    public void Unresolved_reference_emits_reference_fact_with_null_resolved()
    {
        var reference = new GameReference("Missing", GameSymbolKind.XmlObject, "SpaceUnit",
            "file:///a.xml", 3, 0, 7);

        var index = BuildIndex([], [reference]);

        var facts = Sut.Produce("file:///a.xml", index).OfType<XmlReferenceFact>().ToList();
        var f = Assert.Single(facts);
        Assert.Equal("Missing", f.TargetId);
        Assert.Null(f.Resolved);
    }

    [Fact]
    public void SameLayerDifferentType_SameId_DoesNotEmitDuplicateFact()
    {
        // Two symbols with the same ID but different TypeNames in the same layer.
        // The engine allows this; cross-type same-ID is a shadow (warning), not a duplicate (error).
        var faction = new GameSymbol("REBEL", GameSymbolKind.XmlObject, "Faction",
            new FileOrigin("file:///leaf/factions.xml", 0, 0), null);
        var unit = new GameSymbol("REBEL", GameSymbolKind.XmlObject, "SpaceUnit",
            new FileOrigin("file:///leaf/units.xml", 0, 0), null);

        var factionDoc = new DocumentIndex("file:///leaf/factions.xml", 1,
            ImmutableArray.Create(faction), ImmutableArray<GameReference>.Empty,
            LayerRank: 1, LayerName: "Leaf");
        var unitDoc = new DocumentIndex("file:///leaf/units.xml", 1,
            ImmutableArray.Create(unit), ImmutableArray<GameReference>.Empty,
            LayerRank: 1, LayerName: "Leaf");

        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("REBEL", ImmutableArray.Create(faction, unit));

        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add("file:///leaf/factions.xml", factionDoc)
                .Add("file:///leaf/units.xml", unitDoc),
            defs,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        Assert.DoesNotContain(Sut.Produce("file:///leaf/factions.xml", index), f => f is XmlSymbolFact);
        Assert.DoesNotContain(Sut.Produce("file:///leaf/units.xml", index), f => f is XmlSymbolFact);
    }

    [Fact]
    public void TypedReference_ResolvesPreferMatchingTypeName()
    {
        // A reference that declares ExpectedTypeName="Faction" should resolve to the Faction
        // definition even when a higher-ranked SpaceUnit with the same ID exists.
        var faction = new GameSymbol("REBEL", GameSymbolKind.XmlObject, "Faction",
            new FileOrigin("file:///dep/factions.xml", 0, 0), null);
        var unit = new GameSymbol("REBEL", GameSymbolKind.XmlObject, "SpaceUnit",
            new FileOrigin("file:///leaf/units.xml", 0, 0), null);

        var factionDoc = new DocumentIndex("file:///dep/factions.xml", 1,
            ImmutableArray.Create(faction), ImmutableArray<GameReference>.Empty, LayerRank: 0);
        var unitDoc = new DocumentIndex("file:///leaf/units.xml", 1,
            ImmutableArray.Create(unit), ImmutableArray<GameReference>.Empty, LayerRank: 1);

        // The reference is in a third document
        var reference = new GameReference("REBEL", GameSymbolKind.XmlObject, "Faction",
            "file:///leaf/refs.xml", 5, 0, 5);
        var refDoc = new DocumentIndex("file:///leaf/refs.xml", 1,
            ImmutableArray<GameSymbol>.Empty, ImmutableArray.Create(reference), LayerRank: 1);

        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("REBEL", ImmutableArray.Create(faction, unit));

        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add("file:///dep/factions.xml", factionDoc)
                .Add("file:///leaf/units.xml", unitDoc)
                .Add("file:///leaf/refs.xml", refDoc),
            defs,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var facts = Sut.Produce("file:///leaf/refs.xml", index).OfType<XmlReferenceFact>().ToList();
        var f = Assert.Single(facts);
        Assert.NotNull(f.Resolved);
        Assert.Equal("Faction", f.Resolved!.TypeName);
    }

    [Fact]
    public void TypedReference_FallsBackToUntypedWhenNoTypeMatch()
    {
        // When no symbol with the expected TypeName exists, fall back to the untyped winner
        // so TypeMismatchHandler fires rather than UnresolvedReferenceHandler.
        var unit = new GameSymbol("REBEL", GameSymbolKind.XmlObject, "SpaceUnit",
            new FileOrigin("file:///leaf/units.xml", 0, 0), null);
        var unitDoc = new DocumentIndex("file:///leaf/units.xml", 1,
            ImmutableArray.Create(unit), ImmutableArray<GameReference>.Empty, LayerRank: 1);

        // Reference expects "Planet" but no Planet exists
        var reference = new GameReference("REBEL", GameSymbolKind.XmlObject, "Planet",
            "file:///leaf/refs.xml", 5, 0, 5);
        var refDoc = new DocumentIndex("file:///leaf/refs.xml", 1,
            ImmutableArray<GameSymbol>.Empty, ImmutableArray.Create(reference), LayerRank: 1);

        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("REBEL", ImmutableArray.Create(unit));

        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add("file:///leaf/units.xml", unitDoc)
                .Add("file:///leaf/refs.xml", refDoc),
            defs,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var facts = Sut.Produce("file:///leaf/refs.xml", index).OfType<XmlReferenceFact>().ToList();
        var f = Assert.Single(facts);
        // Not null: falls back to untyped SpaceUnit; TypeMismatchHandler will handle the mismatch
        Assert.NotNull(f.Resolved);
        Assert.Equal("SpaceUnit", f.Resolved!.TypeName);
    }
}