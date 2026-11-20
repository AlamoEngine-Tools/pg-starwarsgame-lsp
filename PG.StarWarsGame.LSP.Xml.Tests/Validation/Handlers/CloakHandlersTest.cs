// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     The cloak diagnostics' severities: a warning where the cloak changes nothing at all, and
///     information where it still fades or tints the unit.
/// </summary>
public sealed class CloakHandlersTest
{
    private const string Uri = "file:///units.xml";

    [Fact]
    public void HidesNothing_WithALookThatChangesSomething_IsInformation()
    {
        var result = Assert.Single(new CloakHidesNothingHandler().Handle(
            new CloakHidesNothingFact(Uri, 0, 0, 1, "UNIT", "unit.alo", "Stealth_Ability", false),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(XmlDiagnosticSeverity.Information, result.Severity);
        Assert.Equal("No part of 'unit.alo' has BASEMESH in its name, so <Stealth_Ability> hides no part.",
            result.Message);
    }

    [Fact]
    public void HidesNothing_WithALookThatChangesNothing_IsAWarning()
    {
        var result = Assert.Single(new CloakHidesNothingHandler().Handle(
            new CloakHidesNothingFact(Uri, 0, 0, 1, "UNIT", "unit.alo", "Stealth_Ability", true),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(XmlDiagnosticSeverity.Warning, result.Severity);
        Assert.Contains("leaves the look unchanged", result.Message);
    }

    [Fact]
    public void ShowsNoShell_WithALookThatChangesSomething_IsInformation()
    {
        var result = Assert.Single(new CloakShowsNoShellHandler().Handle(
            new CloakShowsNoShellFact(Uri, 0, 0, 1, "UNIT", "unit.alo", "Force_Cloak_Ability", false),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(XmlDiagnosticSeverity.Information, result.Severity);
        Assert.Equal("No part of 'unit.alo' has STEALTH in its name, so <Force_Cloak_Ability> shows no part.",
            result.Message);
    }

    [Fact]
    public void ShowsNoShell_WithALookThatChangesNothing_IsAWarning()
    {
        var result = Assert.Single(new CloakShowsNoShellHandler().Handle(
            new CloakShowsNoShellFact(Uri, 0, 0, 1, "UNIT", "unit.alo", "Stealth_Ability", true),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(XmlDiagnosticSeverity.Warning, result.Severity);
    }

    [Fact]
    public void ForceCloakKeepsAttachedModel_IsInformation()
    {
        var result = Assert.Single(new ForceCloakKeepsAttachedModelHandler().Handle(
            new ForceCloakKeepsAttachedModelFact(Uri, 0, 0, 1, "UNIT", "HP_TURRET", "turret.alo"),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(XmlDiagnosticSeverity.Information, result.Severity);
        Assert.Equal("Hardpoint 'HP_TURRET' attaches 'turret.alo', which <Force_Cloak_Ability> leaves drawn.",
            result.Message);
    }
}