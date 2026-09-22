// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Diagnostics;

public sealed class ReferenceResolutionEvaluatorTest
{
    private static GameSymbol Symbol(string id, string typeName)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new FileOrigin("file:///a.xml", 0, null), null);
    }

    // ── kind references ───────────────────────────────────────────────────────
    //
    // A planet slot asks what the object IS. The index types a planet "GameObjectType" like
    // everything else in its file, so comparing type names reported a mismatch on every correct
    // planet reference in the corpus - the bug this branch exists to end.

    private static readonly ObjectKindDefinition PlanetKind = new()
    {
        Kind = "Planet", Behaviors = ["PLANET"]
    };

    private static GameIndex IndexWith(string id, params string[] behaviors)
    {
        var symbol = new GameSymbol(id, GameSymbolKind.XmlObject, "GameObjectType",
            new UnknownOrigin("test"), null, null, behaviors);
        return GameIndex.Empty with
        {
            WorkspaceDefinitions = ImmutableDictionary
                .Create<string, ImmutableArray<GameSymbol>>(StringComparer.OrdinalIgnoreCase)
                .Add(id, [symbol])
        };
    }

    [Fact]
    public void Evaluate_KindSatisfied_IsSilentDespiteTheTypeName()
    {
        var index = IndexWith("Kashyyyk", "PLANET");

        var result = ReferenceResolutionEvaluator.Evaluate("Kashyyyk", "Planet",
            index.Resolve("Kashyyyk"), expectedKind: PlanetKind, index: index);

        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_KindNotSatisfied_NamesTheMissingBehaviour()
    {
        var index = IndexWith("X_Wing", "DUMMY_STARSHIP");

        var result = ReferenceResolutionEvaluator.Evaluate("X_Wing", "Planet",
            index.Resolve("X_Wing"), expectedKind: PlanetKind, index: index);

        Assert.NotNull(result);
        Assert.Equal(XmlDiagnosticSeverity.Error, result!.Value.Severity);
        Assert.Contains("Expected a Planet", result.Value.Message);
        Assert.Contains("PLANET behaviour", result.Value.Message);
    }

    // The predicate cannot be judged without the index, and guessing would put an error on correct
    // data. Silence is the only safe answer.
    [Fact]
    public void Evaluate_KindWithoutAnIndex_IsSilent()
    {
        var result = ReferenceResolutionEvaluator.Evaluate("X_Wing", "Planet",
            Symbol("X_Wing", "GameObjectType"), expectedKind: PlanetKind);

        Assert.Null(result);
    }

    // A baseline built before behaviours answers nothing for every shipped object. Reporting those
    // would be an error on every correct reference into the base game.
    [Fact]
    public void Evaluate_KindUnjudgeable_IsSilent()
    {
        var index = IndexWith("Vader");
        var heroKind = new ObjectKindDefinition { Kind = "HeroUnit", Flags = ["Is_Named_Hero"] };

        var result = ReferenceResolutionEvaluator.Evaluate("Vader", "HeroUnit",
            index.Resolve("Vader"), expectedKind: heroKind, index: index);

        Assert.Null(result);
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

    // ── unresolved because the TYPE is not indexed at all ─────────────────────
    //
    // "No object with this name exists in the workspace" is a claim we cannot make about a type we
    // never read: the AI tree is excluded on purpose (EaWXmlContext), and GRAPHICDETAILS.XML emits
    // no symbols yet, so the object IS there and we skipped it. Zero indexed instances of a
    // schema-declared type is the signal - it needs no list to maintain and it stops being true by
    // itself the moment the type starts indexing.

    [Fact]
    public void Evaluate_Unresolved_ExpectedTypeHasNoIndexedInstances_IsInformation()
    {
        var result = ReferenceResolutionEvaluator.Evaluate("BasicEmpire", "AIPlayerType", null,
            indexedTypeNames: new HashSet<string> { "Faction", "GameObjectType" });

        Assert.NotNull(result);
        Assert.Equal(XmlDiagnosticSeverity.Information, result!.Value.Severity);
    }

    [Fact]
    public void Evaluate_Unresolved_ExpectedTypeHasNoIndexedInstances_CarriesItsOwnId()
    {
        // Never UnresolvedReference: suppressing "we cannot check this yet" must not suppress every
        // genuine unresolved reference along with it.
        var result = ReferenceResolutionEvaluator.Evaluate("BasicEmpire", "AIPlayerType", null,
            indexedTypeNames: new HashSet<string> { "Faction" });

        Assert.Equal(DiagnosticIds.ReferenceTypeNotIndexed, result!.Value.Id);
    }

    [Fact]
    public void Evaluate_Unresolved_ExpectedTypeHasNoIndexedInstances_MessageDoesNotBlameTheAuthor()
    {
        var result = ReferenceResolutionEvaluator.Evaluate("BasicEmpire", "AIPlayerType", null,
            indexedTypeNames: new HashSet<string> { "Faction" });

        Assert.DoesNotContain("No object with this name exists", result!.Value.Message);
        Assert.Contains("AIPlayerType", result.Value.Message);
    }

    [Fact]
    public void Evaluate_Unresolved_ExpectedTypeIsIndexed_StaysAnError()
    {
        // The type indexes fine and the name is still missing: that is a typo, and it keeps the
        // error it has always had.
        var result = ReferenceResolutionEvaluator.Evaluate("MISPELLED", "Faction", null,
            indexedTypeNames: new HashSet<string> { "Faction" });

        Assert.Equal(XmlDiagnosticSeverity.Error, result!.Value.Severity);
        Assert.Equal(DiagnosticIds.UnresolvedReference, result.Value.Id);
    }

    [Fact]
    public void Evaluate_Unresolved_EmptyTypeIndex_StaysAnError()
    {
        // An empty index is "nothing has been indexed yet" - startup, or a fixture - not "this type
        // is unsupported". Reading it the other way would downgrade every missing reference in the
        // workspace to a notice until indexing finished.
        var result = ReferenceResolutionEvaluator.Evaluate("MISSING", "SpaceUnit", null,
            indexedTypeNames: new HashSet<string>());

        Assert.Equal(XmlDiagnosticSeverity.Error, result!.Value.Severity);
        Assert.Equal(DiagnosticIds.UnresolvedReference, result.Value.Id);
    }

    [Fact]
    public void Evaluate_Unresolved_NoTypeIndexSupplied_BehavesAsBefore()
    {
        // Every caller that has not been taught about the type index keeps today's behaviour.
        var result = ReferenceResolutionEvaluator.Evaluate("BasicEmpire", "AIPlayerType", null);

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