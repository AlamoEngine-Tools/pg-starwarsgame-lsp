// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

public sealed class XmlLayerShadowFactProducerTest
{
    private const string LeafUri = "file:///leaf/units.xml";
    private const string DepUri = "file:///dep/units.xml";
    private const string DepUri2 = "file:///dep2/factions.xml";

    private static GameSymbol XmlSym(string id, string typeName, string uri, int line = 0)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new FileOrigin(uri, line, 0), null);
    }

    private static GameSymbol BaselineSym(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName,
            new MegArchiveOrigin("archive.meg", "units.xml", null, null), null);
    }

    private static DocumentIndex Doc(string uri, int rank, string? layerName, params GameSymbol[] symbols)
    {
        return new DocumentIndex(uri, 1,
            symbols.ToImmutableArray(),
            ImmutableArray<GameReference>.Empty,
            LayerRank: rank,
            LayerName: layerName);
    }

    private static GameIndex BuildIndex(
        BaselineIndex? baseline = null,
        params DocumentIndex[] docs)
    {
        var docDict = docs.ToImmutableDictionary(d => d.DocumentUri, StringComparer.Ordinal);

        var defsBuilder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<GameSymbol>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var sym in docs.SelectMany(d => d.Symbols))
            if (defsBuilder.TryGetValue(sym.Id, out var existing))
                defsBuilder[sym.Id] = existing.Add(sym);
            else
                defsBuilder[sym.Id] = ImmutableArray.Create(sym);

        return GameIndex.Empty with
        {
            Baseline = baseline ?? BaselineIndex.Empty,
            Documents = docDict,
            WorkspaceDefinitions = defsBuilder.ToImmutable()
        };
    }

    private static IReadOnlyList<XmlFact> Produce(string documentUri, string text, GameIndex index)
    {
        return new XmlLayerShadowFactProducer().Produce(documentUri, text, index);
    }

    [Fact]
    public void Produce_FlatWorkspace_NoLayerRank_EmitsNoCrossLayerFact()
    {
        var sym = XmlSym("REBEL", "Unit", LeafUri);
        var leafDoc = Doc(LeafUri, 0, null, sym);
        var index = BuildIndex(null, leafDoc);

        var facts = Produce(LeafUri, @"<Units><Unit Name=""REBEL""/></Units>", index);

        Assert.Empty(facts.OfType<XmlLayerShadowFact>());
    }

    [Fact]
    public void Produce_DepLayerDocument_EmitsNoCrossLayerFact()
    {
        var depSym = XmlSym("UNIT_A", "Unit", DepUri, 5);
        var leafSym = XmlSym("UNIT_A", "Unit", LeafUri);
        var depDoc = Doc(DepUri, 0, "Core", depSym);
        var leafDoc = Doc(LeafUri, 1, "Mod", leafSym);
        var index = BuildIndex(null, depDoc, leafDoc);

        // Producing for the dep document - warning belongs on the leaf side
        var facts = Produce(DepUri, @"<Units><Unit Name=""UNIT_A""/></Units>", index);

        Assert.Empty(facts.OfType<XmlLayerShadowFact>());
    }

    [Fact]
    public void Produce_LeafSymbolWithDepShadow_EmitsCrossLayerShadowFact()
    {
        var leafSym = XmlSym("UNIT_A", "Unit", LeafUri);
        var depSym = XmlSym("UNIT_A", "Unit", DepUri, 5);
        var leafDoc = Doc(LeafUri, 1, "Mod", leafSym);
        var depDoc = Doc(DepUri, 0, "Core", depSym);
        var index = BuildIndex(null, leafDoc, depDoc);

        var facts = Produce(LeafUri, @"<Units><Unit Name=""UNIT_A""/></Units>", index);

        var shadow = Assert.Single(facts.OfType<XmlLayerShadowFact>());
        Assert.Equal("UNIT_A", shadow.SymbolId);
        Assert.Equal("Core", shadow.ShadowedLayerName);
    }

    [Fact]
    public void Produce_LeafSymbolWithBaselineShadow_EmitsNoCrossLayerFact()
    {
        var leafSym = XmlSym("UNIT_B", "Unit", LeafUri);
        var leafDoc = Doc(LeafUri, 1, "Mod", leafSym);
        var baseline = BaselineIndex.Empty with
        {
            Symbols = ImmutableDictionary<string, GameSymbol>.Empty
                .Add("UNIT_B", BaselineSym("UNIT_B", "Unit"))
        };
        var index = BuildIndex(baseline, leafDoc);

        var facts = Produce(LeafUri, @"<Units><Unit Name=""UNIT_B""/></Units>", index);

        Assert.Empty(facts.OfType<XmlLayerShadowFact>());
    }

    [Fact]
    public void Produce_CrossLayerShadow_SuppressionCommentPresent_EmitsNoFact()
    {
        var leafSym = XmlSym("UNIT_A", "Unit", LeafUri, 1);
        var depSym = XmlSym("UNIT_A", "Unit", DepUri, 5);
        var leafDoc = Doc(LeafUri, 1, "Mod", leafSym);
        var depDoc = Doc(DepUri, 0, "Core", depSym);
        var index = BuildIndex(null, leafDoc, depDoc);

        const string text = @"<Units><!-- <Override Name=""UNIT_A""/> --><Unit Name=""UNIT_A""/></Units>";
        var facts = Produce(LeafUri, text, index);

        Assert.Empty(facts.OfType<XmlLayerShadowFact>());
    }

    [Fact]
    public void Produce_SuppressionComment_CaseInsensitiveNameMatch()
    {
        var leafSym = XmlSym("UNIT_A", "Unit", LeafUri, 1);
        var depSym = XmlSym("UNIT_A", "Unit", DepUri, 5);
        var leafDoc = Doc(LeafUri, 1, "Mod", leafSym);
        var depDoc = Doc(DepUri, 0, "Core", depSym);
        var index = BuildIndex(null, leafDoc, depDoc);

        // Suppression comment uses lowercase name - must still match UNIT_A
        const string text = @"<Units><!-- <Override Name=""unit_a""/> --><Unit Name=""UNIT_A""/></Units>";
        var facts = Produce(LeafUri, text, index);

        Assert.Empty(facts.OfType<XmlLayerShadowFact>());
    }

    /// <summary>
    ///     Every type is an id space of its own: a faction below a unit of the same name is another
    ///     object, neither overridden by the unit nor in its way.
    /// </summary>
    [Fact]
    public void Produce_ASameNamedObjectOfAnotherTypeBelow_IsNoShadowOfAnyKind()
    {
        var unitSym = XmlSym("REBEL", "Unit", LeafUri);
        var factionSym = XmlSym("REBEL", "Faction", DepUri, 3);
        var leafDoc = Doc(LeafUri, 1, "Mod", unitSym);
        var depDoc = Doc(DepUri, 0, "Core", factionSym);
        var index = BuildIndex(null, leafDoc, depDoc);

        var facts = Produce(LeafUri, @"<Units><Unit Name=""REBEL""/></Units>", index);

        Assert.Empty(facts);
    }

    /// <summary>The override below the leaf is the definition of the leaf's own type, not another type's.</summary>
    [Fact]
    public void Produce_TheShadowedDefinition_IsOfTheLeafsOwnType()
    {
        var unitSym = XmlSym("REBEL", "Unit", LeafUri);
        var factionSym = XmlSym("REBEL", "Faction", DepUri, 3);
        var depUnitSym = XmlSym("REBEL", "Unit", DepUri2, 7);
        var leafDoc = Doc(LeafUri, 1, "Mod", unitSym);
        var depDoc = Doc(DepUri, 0, "Core", factionSym);
        var depDoc2 = Doc(DepUri2, 0, "Units", depUnitSym);
        var index = BuildIndex(null, leafDoc, depDoc, depDoc2);

        var facts = Produce(LeafUri, @"<Units><Unit Name=""REBEL""/></Units>", index);

        Assert.Equal("Units", Assert.Single(facts.OfType<XmlLayerShadowFact>()).ShadowedLayerName);
    }
}