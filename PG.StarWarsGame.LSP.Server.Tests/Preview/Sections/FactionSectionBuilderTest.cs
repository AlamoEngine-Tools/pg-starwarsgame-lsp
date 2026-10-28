// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Preview;
using PG.StarWarsGame.LSP.Server.Preview.Sections;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview.Sections;

/// <summary>The faction section on its own (#314): every Faction object, in XML order, with its colours, once per snapshot.</summary>
public sealed class FactionSectionBuilderTest
{
    private static GameSymbol At(string id, string typeName, string file, int line)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new FileOrigin(file, line, 0), null, null);
    }

    private static GameIndex Index(params GameSymbol[] symbols)
    {
        return GameIndex.Empty with
        {
            WorkspaceDefinitions = symbols.ToImmutableDictionary(
                s => s.Id, s => ImmutableArray.Create(s), StringComparer.OrdinalIgnoreCase)
        };
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }

    [Fact]
    public void Factions_ListsEveryFactionInTheOrderTheXmlDeclaresThem_WithItsColours()
    {
        var index = Index(
            At("Rebel", "Faction", "file:///factions.xml", 40),
            At("Empire", "Faction", "file:///factions.xml", 10),
            At("Xwing", "SpaceUnit", "file:///units.xml", 1));
        var tags = new FakeVariantTagSource()
            .With("Empire", Tag("Color", "0, 0, 255, 255"), Tag("Display_Font_Color", "1, 2, 3"))
            .With("Rebel", Tag("Color", "255, 0, 0"), Tag("No_Colorization_Color", "9, 9, 9"));

        var factions = new FactionSectionBuilder(new SchemaProviderBuilder().Build(), tags).Factions(index);

        Assert.Equal(["Empire", "Rebel"], factions.Select(f => f.Name));
        Assert.Equal(new PreviewRgba(0, 0, 255, 255), factions[0].Color);
        Assert.Equal(new PreviewRgba(1, 2, 3, 255), factions[0].DisplayFontColor);
        Assert.Null(factions[0].NoColorizationColor);
        Assert.Equal(new PreviewRgba(255, 0, 0, 255), factions[1].Color);
        Assert.Equal(new PreviewRgba(9, 9, 9, 255), factions[1].NoColorizationColor);
    }

    [Fact]
    public void Factions_AreReadOncePerIndexSnapshot()
    {
        var index = Index(At("Rebel", "Faction", "file:///factions.xml", 1));
        var builder = new FactionSectionBuilder(new SchemaProviderBuilder().Build(),
            new FakeVariantTagSource().With("Rebel", Tag("Color", "1, 1, 1")));

        var first = builder.Factions(index);
        var second = builder.Factions(index);
        var other = builder.Factions(index with { Documents = index.Documents });

        Assert.Same(first, second);
        Assert.NotSame(first, other);
        Assert.Equal(first, other);
    }
}
