// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>The two damage type tail facts become the errors the publisher used to build itself (#307).</summary>
public sealed class DamageTypeTailHandlersTest
{
    private const string Uri = "file:///gameconstants.xml";

    private static readonly DiagnosticsContext Ctx =
        new(new SchemaProviderBuilder().Build(), GameIndex.Empty, Uri, "en") { Messages = TestCatalogues.English };

    [Fact]
    public void IncompleteList_IsAnErrorNamingBothCounts()
    {
        IXmlDiagnosticsHandler handler = new DamageTypesIncompleteHandler();

        var result = Assert.Single(handler.Handle(new DamageTypesIncompleteFact(Uri, 0, 10, 11, 2, 20), Ctx));

        Assert.Equal(XmlDiagnosticSeverity.Error, result.Severity);
        Assert.Contains("2", result.Message);
        Assert.Contains("20", result.Message);
        Assert.Equal(DiagnosticIds.DamageTypesIncomplete, handler.DefaultId);
    }

    [Fact]
    public void EntryOutOfOrder_IsAnErrorNamingTheEntryAndTheExpectedOne()
    {
        IXmlDiagnosticsHandler handler = new DamageTypeOutOfOrderHandler();

        var result = Assert.Single(handler.Handle(
            new DamageTypeOutOfOrderFact(Uri, 0, 10, 11, "Damage_Fire", "Damage_Normal", 20), Ctx));

        Assert.Equal(XmlDiagnosticSeverity.Error, result.Severity);
        Assert.Contains("Damage_Fire", result.Message);
        Assert.Contains("Damage_Normal", result.Message);
        Assert.Equal(DiagnosticIds.DamageTypesOutOfOrder, handler.DefaultId);
    }
}