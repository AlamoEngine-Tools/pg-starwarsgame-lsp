// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Providers;
using PG.StarWarsGame.LSP.Schema.Yaml;

namespace PG.StarWarsGame.LSP.Schema.Tests;

/// <summary>
///     <c>kinds.yaml</c> says what makes a game object a planet, a star base, a squadron. The engine
///     never reads the element name; it tests behaviour bits from the <c>&lt;Behavior&gt;</c> list
///     and two hero flags, so every kind must be a predicate over those - and every name the tag
///     and enum files use as a <c>referenceType</c> must be either a real type or such a kind,
///     otherwise the reference resolves to nothing and completion has nothing to propose.
/// </summary>
/// <remarks>
///     Measured 2026-09-21: the engine's behaviour lookup table has 116 tokens, each mapping to
///     exactly one behaviour bit; the type-level kind test reads the bitmap built from
///     <c>&lt;Behavior&gt;</c> alone. Of the shipped corpus, every one of the 110 planets carries
///     PLANET there, and objects that carry a kind token only in a mode list (12 orbital
///     structures in <c>&lt;SpaceBehavior&gt;</c>) are not that kind to the engine.
/// </remarks>
public sealed class EawSchemaObjectKindsTest
{
    /// <summary>The engine's behaviour vocabulary - the XML tokens its lookup table accepts.</summary>
    private static readonly HashSet<string> EngineBehaviorTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "LAND_TEAM_CONTAINER_LOCOMOTOR", "LAND_TEAM_INFANTRY_LOCOMOTOR", "SIMPLE_LOCOMOTOR",
        "STARSHIP_LOCOMOTOR", "SIMPLE_SPACE_LOCOMOTOR", "FLEET_LOCOMOTOR", "SELECTABLE", "PLANET",
        "ASTEROID_FIELD_DAMAGE", "FLEET", "TRANSPORT", "PRODUCTION", "IDLE", "PROJECTILE", "PARTICLE",
        "SURFACE_FX", "WEAPON", "TURRET", "TARGETING", "TEAM_TARGETING", "STARSHIP_COMBATANT",
        "WALK_LOCOMOTOR", "LAND_OBSTACLE", "SQUAD_LEADER", "DEATH", "BURNING", "BIKE_LOCOMOTOR",
        "POWERED", "SHIELDED", "MARKER", "WALL", "REVEAL", "HIDE_WHEN_FOGGED", "TREAD_SCROLL",
        "WIND_DISTURBANCE", "UNIT_AI", "TEAM", "SPAWN_SQUADRON", "DEPLOY_TROOPERS", "LANDING",
        "SPAWN_INDIGENOUS_UNITS", "INDIGENOUS_UNIT", "HUNT", "GUARD_SPAWN_HOUSE", "BARRAGE", "LURE",
        "EXPLOSION", "SQUASH", "AMBIENT_SFX", "DEBRIS", "TACTICAL_BUILD_OBJECTS",
        "TACTICAL_UNDER_CONSTRUCTION", "TACTICAL_SELL", "TACTICAL_SUPER_WEAPON", "FIGHTER_LOCOMOTOR",
        "TEAM_LOCOMOTOR", "FLYING_LOCOMOTOR", "SPECIAL_WEAPON", "TELEKINESIS_TARGET",
        "EARTHQUAKE_TARGET", "VEHICLE_THIEF", "DEMOLITION", "BOMB", "JETPACK_LOCOMOTOR",
        "TRANSPORT_LANDING", "LOBBING_SUPERWEAPON", "STUNNABLE", "LANDBOMBER_LOCOMOTOR", "BASE_SHIELD",
        "GRAVITY_CONTROL_FIELD", "SKY_DOME", "CASH_POINT", "CAPTURE_POINT", "DAMAGE_TRACKING",
        "TERRAIN_TEXTURE_MODIFICATION", "IMPOSING_PRESENCE", "HARASS", "AVOID_DANGER", "SELF_DESTRUCT",
        "ION_STUN_EFFECT", "ABILITY_COUNTDOWN", "AFFECTED_BY_SHIELD", "NEBULA", "REINFORCEMENT_POINT",
        "HINT", "INVULNERABLE", "SPACE_OBSTACLE", "MULTIPLAYER_BEACON", "HOLSTER_WEAPON",
        "GARRISON_VEHICLE", "GARRISON_STRUCTURE", "GARRISON_UNIT", "DISABLE_FORCE_ABILITIES", "CONFUSE",
        "INFECTION", "PROXIMITY_MINE", "GARRISON_HOVER", "BUZZ_DROIDS", "CORRUPT_SYSTEMS", "BOARDABLE",
        "DYNAMIC_TRANSFORM", "DUMMY_GROUND_BASE", "DUMMY_STAR_BASE", "DUMMY_GROUND_COMPANY",
        "DUMMY_SPACE_FIGHTER_SQUADRON", "DUMMY_GROUND_STRUCTURE", "DUMMY_ORBITAL_STRUCTURE",
        "DUMMY_STARSHIP", "DUMMY_LAND_BASE_LEVEL_COMPONENT", "DUMMY_SPECIAL_WEAPON_SOURCE",
        "DUMMY_TACTICAL_SUPER_WEAPON_TARGET", "DUMMY_UPGRADE", "DUMMY_DESTROY_AFTER_BOMBING_RUN",
        "DUMMY_DESTROY_AFTER_SPECIAL_WEAPON_FIRED", "DUMMY_TOOLTIP", "DUMMY_DESTROY_AFTER_PLANETARY_BOMBARD"
    };

    private static readonly Lazy<IReadOnlyList<ObjectKindDefinition>> Kinds =
        new(() => YamlSchemaParser.ParseKindFile(EawSchemaRepo.Read("kinds.yaml")));

    /// <summary>
    ///     End to end on the shipped data: the file parses in isolation in every other test here,
    ///     which would still pass if the provider never looked at <c>kinds.yaml</c> at all.
    /// </summary>
    [Fact]
    public void TheShippedSchemaDirectory_LoadsItsKinds()
    {
        using var provider = new LocalFileSchemaProvider(
            EawSchemaRepo.Root, new FileSystem(), NullLogger<LocalFileSchemaProvider>.Instance);

        Assert.NotEmpty(provider.AllKinds);
        Assert.Equal(Kinds.Value.Count, provider.AllKinds.Count);
        Assert.Contains("PLANET", provider.GetKind("Planet")!.Behaviors);
    }

    [Fact]
    public void EveryKindHasAPredicate()
    {
        var empty = Kinds.Value.Where(k => !k.HasPredicate).Select(k => k.Kind).ToList();

        Assert.True(empty.Count == 0,
            "Kinds with no behaviour, flag or membership predicate match nothing: " + string.Join(", ", empty));
    }

    [Fact]
    public void EveryBehaviorNamedByAKindIsAnEngineToken()
    {
        var unknown = Kinds.Value
            .SelectMany(k => k.Behaviors.Select(b => (k.Kind, Behavior: b)))
            .Where(x => !EngineBehaviorTokens.Contains(x.Behavior))
            .Select(x => $"{x.Kind}: {x.Behavior}")
            .ToList();

        Assert.True(unknown.Count == 0,
            "Behaviour tokens the engine's lookup table does not know: " + string.Join(", ", unknown));
    }

    [Fact]
    public void KindNamesAreUniqueAndDoNotShadowATypeName()
    {
        var types = YamlSchemaParser.ParseTypeFile(EawSchemaRepo.Read("types.yaml"))
            .Select(t => t.TypeName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var duplicates = Kinds.Value.GroupBy(k => k.Kind, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        var shadowing = Kinds.Value.Where(k => types.Contains(k.Kind)).Select(k => k.Kind).ToList();

        Assert.True(duplicates.Count == 0, "Duplicate kinds: " + string.Join(", ", duplicates));
        Assert.True(shadowing.Count == 0,
            "A kind must not share its name with a types.yaml type, or a referenceType would be ambiguous: "
            + string.Join(", ", shadowing));
    }

    /// <summary>
    ///     The contract the kinds exist for: a <c>referenceType</c> that names neither a type nor a
    ///     kind resolves to nothing. Story-scoped pseudo types are resolved by the story layer and
    ///     are exempt.
    /// </summary>
    [Fact]
    public void EveryObjectReferenceTypeIsATypeOrAKind()
    {
        var known = YamlSchemaParser.ParseTypeFile(EawSchemaRepo.Read("types.yaml"))
            .Select(t => t.TypeName)
            .Concat(Kinds.Value.Select(k => k.Kind))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dangling = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in EawSchemaRepo.YamlFiles("tags"))
        foreach (var tag in YamlSchemaParser.ParseTagFile(EawSchemaRepo.Read(file)))
            if (tag.ReferenceKind == ReferenceKind.XmlObject && tag.ReferenceType is { } rt && !known.Contains(rt))
                dangling.Add($"{file} {tag.Tag} -> {rt}");

        foreach (var file in EawSchemaRepo.YamlFiles("enums"))
        {
            var definition = YamlSchemaParser.ParseEnumFile(EawSchemaRepo.Read(file));
            foreach (var value in definition.Values)
            foreach (var param in value.Params ?? [])
                if (param.ReferenceType is { } rt
                    && param.ReferenceKind is ReferenceKind.None or ReferenceKind.XmlObject
                    && !StoryReferenceTypes.IsStoryScoped(rt)
                    && rt != StoryReferenceTypes.ThreadFileTypeName
                    && !known.Contains(rt))
                    dangling.Add($"{file} {value.Name}[{param.Position}] -> {rt}");
        }

        Assert.True(dangling.Count == 0,
            "referenceType names that are neither a types.yaml type nor a kinds.yaml kind:\n  "
            + string.Join("\n  ", dangling));
    }
}