// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     Two game objects whose names share a CRC-32 are one entry to the engine: only the one loaded
///     first can be referenced, and which one that is follows the load order - so both are told, and
///     neither is told it is the one that loses.
/// </summary>
public sealed class NameCrcCollisionHandlerTest
{
    private static XmlNameCrcCollisionFact Fact(params GameSymbol[] others)
    {
        return new XmlNameCrcCollisionFact("file:///units.xml", 4, 2, 12, "E2E_CRC_BXO9XL", 0xC492BBC0, others);
    }

    [Fact]
    public void NamesTheOtherObject_AndTheHash_AsAWarning()
    {
        var other = new GameSymbol("E2E_CRC_CDATBA", GameSymbolKind.XmlObject, "GameObjectType",
            new FileOrigin("file:///other.xml", 9, 4), null);

        var d = Assert.Single(new NameCrcCollisionHandler().Handle(Fact(other), XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(
            "'E2E_CRC_BXO9XL' has the same name hash as 'E2E_CRC_CDATBA' (0xC492BBC0). The engine finds objects by that hash, so only the one it loads first can be referenced.",
            d.Message);
        var related = Assert.Single(d.RelatedLocations!);
        Assert.Equal("file:///other.xml", related.Uri);
    }

    [Fact]
    public void AGameObject_IsNamed_ButNotLinked()
    {
        // A shipped object's origin is a game path, which no editor can open.
        var game = new GameSymbol("Darth_Vader", GameSymbolKind.XmlObject, "GameObjectType",
            new FileOrigin("DATA\\XML\\HEROES.XML", 9, 4), null);

        var d = Assert.Single(new NameCrcCollisionHandler().Handle(Fact(game), XmlHandlerTestFixtures.EmptyCtx));

        Assert.Contains("'Darth_Vader'", d.Message);
        Assert.Null(d.RelatedLocations);
    }
}
