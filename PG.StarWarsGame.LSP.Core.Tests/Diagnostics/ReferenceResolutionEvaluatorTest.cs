// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Diagnostics;

public sealed class ReferenceResolutionEvaluatorTest
{
    private static GameSymbol Symbol(string id, string typeName) =>
        new(id, GameSymbolKind.XmlObject, typeName, new FileOrigin("file:///a.xml", 0, null), null);

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
