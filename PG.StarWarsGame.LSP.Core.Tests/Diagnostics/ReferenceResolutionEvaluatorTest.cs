// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Diagnostics;

public sealed class ReferenceResolutionEvaluatorTest
{
    private static GameSymbol Symbol(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new FileOrigin("file:///a.xml", 0, null), null);
    }

    // ── unresolved ────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_Unresolved_ReturnsError()
    {
        var result = ReferenceResolutionEvaluator.Evaluate("UNIT_A", null, null);
        Assert.NotNull(result);
        Assert.Equal(XmlDiagnosticSeverity.Error, result!.Value.Severity);
    }

    [Fact]
    public void Evaluate_Unresolved_MessageContainsTargetId()
    {
        var result = ReferenceResolutionEvaluator.Evaluate("UNIT_A", null, null);
        Assert.Contains("UNIT_A", result!.Value.Message);
    }

    [Fact]
    public void Evaluate_Unresolved_WithExpectedType_StillReturnsError()
    {
        var result = ReferenceResolutionEvaluator.Evaluate("MISSING", "Unit", null);
        Assert.NotNull(result);
        Assert.Equal(XmlDiagnosticSeverity.Error, result!.Value.Severity);
    }

    // ── resolved, no expected type ────────────────────────────────────────────

    [Fact]
    public void Evaluate_Resolved_NoExpectedType_ReturnsNull()
    {
        var result = ReferenceResolutionEvaluator.Evaluate("UNIT_A", null, Symbol("UNIT_A", "Unit"));
        Assert.Null(result);
    }

    // ── GameObjectType wildcard ───────────────────────────────────────────────

    [Fact]
    public void Evaluate_GameObjectType_ReturnsNull()
    {
        var result = ReferenceResolutionEvaluator.Evaluate("UNIT_A", "GameObjectType", Symbol("UNIT_A", "Unit"));
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_GameObjectType_CaseInsensitive_ReturnsNull()
    {
        var result = ReferenceResolutionEvaluator.Evaluate("UNIT_A", "gameobjecttype", Symbol("UNIT_A", "Unit"));
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_GameObjectType_MixedCase_ReturnsNull()
    {
        var result = ReferenceResolutionEvaluator.Evaluate("UNIT_A", "GameObjectType", Symbol("UNIT_A", "GroundUnit"));
        Assert.Null(result);
    }

    // ── correct type ──────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_CorrectType_ReturnsNull()
    {
        var result = ReferenceResolutionEvaluator.Evaluate("UNIT_A", "Unit", Symbol("UNIT_A", "Unit"));
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_CorrectType_CaseInsensitive_ReturnsNull()
    {
        var result = ReferenceResolutionEvaluator.Evaluate("UNIT_A", "unit", Symbol("UNIT_A", "Unit"));
        Assert.Null(result);
    }

    // ── SpecialAbility family allowlist ──────────────────────────────────────

    [Fact]
    public void Evaluate_SpecialAbilityExpected_ConcreteAbilitySubtype_ReturnsNull()
    {
        // ProximityMinesAbility is a concrete SpecialAbility subtype - GUI_Activated_Ability_Name
        // (referenceType: SpecialAbility) must accept it, not just literal "SpecialAbility".
        var result = ReferenceResolutionEvaluator.Evaluate(
            "Bacara_Proximity_Mines_AV", "SpecialAbility",
            Symbol("Bacara_Proximity_Mines_AV", "ProximityMinesAbility"));
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_SpecialAbilityExpected_ConcreteAbilitySubtype_CaseInsensitive_ReturnsNull()
    {
        var result = ReferenceResolutionEvaluator.Evaluate(
            "X", "specialability", Symbol("X", "proximityminesability"));
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_SpecialAbilityExpected_LiteralSpecialAbility_ReturnsNull()
    {
        var result = ReferenceResolutionEvaluator.Evaluate(
            "X", "SpecialAbility", Symbol("X", "SpecialAbility"));
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_SpecialAbilityExpected_UnrelatedType_ReturnsError()
    {
        var result = ReferenceResolutionEvaluator.Evaluate(
            "X", "SpecialAbility", Symbol("X", "Unit"));
        Assert.NotNull(result);
        Assert.Equal(XmlDiagnosticSeverity.Error, result!.Value.Severity);
    }

    [Fact]
    public void Evaluate_NonSpecialAbilityExpected_AbilitySubtype_StillEnforcesExactMatch()
    {
        // The allowlist relaxation is scoped to expectedTypeName == SpecialAbility only - an
        // unrelated expected type must still require an exact match.
        var result = ReferenceResolutionEvaluator.Evaluate(
            "X", "UnitAbility", Symbol("X", "ProximityMinesAbility"));
        Assert.NotNull(result);
        Assert.Equal(XmlDiagnosticSeverity.Error, result!.Value.Severity);
    }

    // ── type mismatch ─────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_TypeMismatch_ReturnsError()
    {
        var result = ReferenceResolutionEvaluator.Evaluate("UNIT_A", "Faction", Symbol("UNIT_A", "Unit"));
        Assert.NotNull(result);
        Assert.Equal(XmlDiagnosticSeverity.Error, result!.Value.Severity);
    }

    [Fact]
    public void Evaluate_TypeMismatch_MessageContainsTargetId()
    {
        var result = ReferenceResolutionEvaluator.Evaluate("UNIT_A", "Faction", Symbol("UNIT_A", "Unit"));
        Assert.Contains("UNIT_A", result!.Value.Message);
    }

    [Fact]
    public void Evaluate_TypeMismatch_MessageContainsBothTypes()
    {
        var result = ReferenceResolutionEvaluator.Evaluate("UNIT_A", "Faction", Symbol("UNIT_A", "Unit"));
        Assert.Contains("Faction", result!.Value.Message);
        Assert.Contains("Unit", result!.Value.Message);
    }
}