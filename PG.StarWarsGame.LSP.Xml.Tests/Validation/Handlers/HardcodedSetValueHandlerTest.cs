// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     A token the set does not know is an error under the unknown-value id; one it marks deprecated
///     is a warning under its own id, so either can be suppressed alone (#307).
/// </summary>
public sealed class HardcodedSetValueHandlerTest
{
    private const string Uri = "file:///units.xml";

    private static readonly DiagnosticsContext Ctx =
        new(new SchemaProviderBuilder().Build(), GameIndex.Empty, Uri, "en") { Messages = TestCatalogues.English };

    private static readonly IXmlDiagnosticsHandler Handler = new HardcodedSetValueHandler();

    [Fact]
    public void UnknownToken_IsAnErrorNamingTheTokenAndTheSet()
    {
        var result = Assert.Single(Handler.Handle(
            new HardcodedSetValueFact(Uri, 3, 20, 11, "TYPO_MODULE", "BehaviorModule", false), Ctx));

        Assert.Equal(XmlDiagnosticSeverity.Error, result.Severity);
        Assert.Contains("TYPO_MODULE", result.Message);
        Assert.Contains("BehaviorModule", result.Message);
        Assert.Equal(DiagnosticIds.UnknownHardcodedSetValue, result.Id);
    }

    [Fact]
    public void DeprecatedToken_IsAWarningUnderItsOwnId()
    {
        var result = Assert.Single(Handler.Handle(
            new HardcodedSetValueFact(Uri, 3, 20, 10, "OLD_MODULE", "BehaviorModule", true), Ctx));

        Assert.Equal(XmlDiagnosticSeverity.Warning, result.Severity);
        Assert.Contains("OLD_MODULE", result.Message);
        Assert.Contains("deprecated", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DiagnosticIds.DeprecatedHardcodedSetValue, result.Id);
    }

    [Fact]
    public void TheFloorIsTheUnknownValueId()
    {
        Assert.Equal(DiagnosticIds.UnknownHardcodedSetValue, Handler.DefaultId);
    }
}