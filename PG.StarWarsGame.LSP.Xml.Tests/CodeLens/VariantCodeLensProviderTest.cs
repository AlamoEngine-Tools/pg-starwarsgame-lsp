// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Newtonsoft.Json.Linq;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.CodeLens;

namespace PG.StarWarsGame.LSP.Xml.Tests.CodeLens;

public sealed class VariantCodeLensProviderTest
{
    private static GameSymbol Sym(string id, string? baseId = null)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "SpaceUnit",
            new FileOrigin($"file:///{id}.xml", 3, 0), null, baseId);
    }

    private static GameIndex IndexWith(params GameSymbol[] symbols)
    {
        var defs = symbols.ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s),
            StringComparer.OrdinalIgnoreCase);
        return GameIndex.Empty with { WorkspaceDefinitions = defs };
    }

    private static CodeLensSymbolContext Ctx(GameSymbol symbol, GameIndex index)
    {
        return new CodeLensSymbolContext(symbol, (FileOrigin)symbol.Origin, index);
    }

    [Fact]
    public void Handle_VariantSymbol_EmitsShowEffectiveLens()
    {
        var v = Sym("V", "B");
        var lens = new VariantCodeLensProvider().Handle(Ctx(v, IndexWith(v, Sym("B"))));

        Assert.NotNull(lens);
        Assert.Equal(VariantCodeLensProvider.ShowEffectiveCommand, lens!.Command!.Name);
        Assert.Contains("variant of B", lens.Command.Title);
        Assert.Equal("V", (string)lens.Command.Arguments![0]!);
    }

    [Fact]
    public void Handle_BaseSymbol_EmitsVariantsLensThatOpensChildrenInReferences()
    {
        var b = Sym("B");
        var index = IndexWith(b, Sym("V1", "B"), Sym("V2", "B"));

        var lens = new VariantCodeLensProvider().Handle(Ctx(b, index));

        Assert.NotNull(lens);
        Assert.Equal("2 variants", lens!.Command!.Title);
        // Clicking must open the references peek (previously the lens had no command and did nothing).
        Assert.Equal("aet-eaw-edit.lsp.showReferences", lens.Command.Name);
        var locations = (JArray)lens.Command.Arguments![2]!;
        Assert.Equal(2, locations.Count);
    }

    [Fact]
    public void Handle_PlainSymbol_ReturnsNull()
    {
        var p = Sym("P");
        Assert.Null(new VariantCodeLensProvider().Handle(Ctx(p, IndexWith(p))));
    }
}
