// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Newtonsoft.Json.Linq;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.CodeLens;

namespace PG.StarWarsGame.LSP.Xml.Tests.CodeLens;

public sealed class OverrideCodeLensProviderTest
{
    private static GameSymbol Sym(string id, string uri, int line)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "SpaceUnit", new FileOrigin(uri, line, 0), null);
    }

    private static GameIndex TwoLayerIndex(out GameSymbol core, out GameSymbol rev)
    {
        core = Sym("UNIT_A", "file:///core/u.xml", 2);
        rev = Sym("UNIT_A", "file:///rev/u.xml", 7);

        var coreDoc = new DocumentIndex("file:///core/u.xml", 1,
            ImmutableArray.Create(core), ImmutableArray<GameReference>.Empty, LayerRank: 0, LayerName: "Core Library");
        var revDoc = new DocumentIndex("file:///rev/u.xml", 1,
            ImmutableArray.Create(rev), ImmutableArray<GameReference>.Empty, LayerRank: 1, LayerName: "Rev");

        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
            .Add("UNIT_A", ImmutableArray.Create(core, rev));

        return new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty
                .Add("file:///core/u.xml", coreDoc)
                .Add("file:///rev/u.xml", revDoc),
            defs,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    [Fact]
    public void Handle_WinningOverride_EmitsLensNamingShadowedLayer_AndOpensShadowedLocation()
    {
        var index = TwoLayerIndex(out _, out var rev);

        var lens = new OverrideCodeLensProvider().Handle(
            new CodeLensSymbolContext(rev, (FileOrigin)rev.Origin, index));

        Assert.NotNull(lens);
        Assert.Equal("aet-eaw-edit.lsp.showReferences", lens!.Command!.Name);
        Assert.Contains("UNIT_A", lens.Command.Title);
        Assert.Contains("Core Library", lens.Command.Title);

        var locations = (JArray)lens.Command.Arguments![2]!;
        Assert.Single(locations);
        Assert.Equal("file:///core/u.xml", (string)locations[0]!["uri"]!);
    }

    [Fact]
    public void Handle_ShadowedDefinition_NotTheWinner_ReturnsNull()
    {
        var index = TwoLayerIndex(out var core, out _);

        var lens = new OverrideCodeLensProvider().Handle(
            new CodeLensSymbolContext(core, (FileOrigin)core.Origin, index));

        Assert.Null(lens);
    }

    [Fact]
    public void Handle_SingleDefinition_ReturnsNull()
    {
        var only = Sym("X", "file:///rev/x.xml", 1);
        var doc = new DocumentIndex("file:///rev/x.xml", 1,
            ImmutableArray.Create(only), ImmutableArray<GameReference>.Empty, LayerRank: 1, LayerName: "Rev");
        var index = new GameIndex(BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add("file:///rev/x.xml", doc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("X", ImmutableArray.Create(only)),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        Assert.Null(new OverrideCodeLensProvider().Handle(
            new CodeLensSymbolContext(only, (FileOrigin)only.Origin, index)));
    }
}