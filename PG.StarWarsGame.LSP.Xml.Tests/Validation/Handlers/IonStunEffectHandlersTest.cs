// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     The ion stun effects a type without the behaviour draws from the start: information that
///     names the model and the effects.
/// </summary>
public sealed class IonStunEffectHandlersTest
{
    private const string Uri = "file:///units.xml";

    [Fact]
    public void NeverHidden_IsInformation_AndNamesTheEffects()
    {
        var handler = new IonEffectsNeverHiddenHandler();

        var result = Assert.Single(handler.Handle(
            new IonEffectsNeverHiddenFact(Uri, 0, 0, 1, "UNIT", "unit.alo", ["pi_sparks", "pi_arc"]),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.Equal(DiagnosticIds.IonEffectsNeverHidden, handler.DefaultId);
        Assert.Equal(XmlDiagnosticSeverity.Information, result.Severity);
        Assert.Equal(
            "Ion stun effects of 'unit.alo' are drawn from the start and never hidden without the ION_STUN_EFFECT behaviour - pi_sparks, pi_arc.",
            result.Message);
    }

    /// <summary>A long list names the first eight and counts the rest.</summary>
    [Fact]
    public void NeverHidden_WithMoreThanEight_CountsTheRest()
    {
        string[] effects = ["pi_a", "pi_b", "pi_c", "pi_d", "pi_e", "pi_f", "pi_g", "pi_h", "pi_i", "pi_j"];

        var result = Assert.Single(new IonEffectsNeverHiddenHandler().Handle(
            new IonEffectsNeverHiddenFact(Uri, 0, 0, 1, "UNIT", "unit.alo", effects),
            XmlHandlerTestFixtures.EmptyCtx));

        Assert.EndsWith("- pi_a, pi_b, pi_c, pi_d, pi_e, pi_f, pi_g, pi_h and 2 more.", result.Message);
    }

    [Fact]
    public void TheIdIsCrossTag37()
    {
        Assert.Equal("aetswg-007-0037", DiagnosticIds.IonEffectsNeverHidden.ToString());
    }
}