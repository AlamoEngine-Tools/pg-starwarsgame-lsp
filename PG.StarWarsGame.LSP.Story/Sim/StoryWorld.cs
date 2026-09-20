// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Story.Sim;

/// <summary>What the simulator knows about a planet. Facts, not a planet object.</summary>
public sealed record StoryPlanetFact(
    string Name,
    string? Owner,
    bool Revealed = false,
    bool Corrupted = false,
    bool Destroyed = false);

/// <summary>A presence fact: how many units of a type a faction has at a planet.</summary>
public sealed record StoryUnitFact(string Type, string Owner, string Planet, int Count);

/// <summary>A flag write attached to a world change (a tactical script setting a global).</summary>
public sealed record StoryFlagWrite(string Flag, int Value);

/// <summary>
///     The simulator's world: a flat fact table, deliberately not the galactic layer. No
///     containers, teams, fleets, movement, production, combat or AI. It is seeded once from the
///     campaign and after that only a reward write or the author's change touches it.
/// </summary>
public sealed record StoryWorld
{
    public static readonly StoryWorld Empty = new();

    public ImmutableDictionary<string, StoryPlanetFact> Planets { get; init; } =
        ImmutableDictionary.Create<string, StoryPlanetFact>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Presence counts keyed by <see cref="UnitKey" />.</summary>
    public ImmutableDictionary<string, int> Units { get; init; } =
        ImmutableDictionary.Create<string, int>(StringComparer.OrdinalIgnoreCase);

    public ImmutableDictionary<string, int> Tech { get; init; } =
        ImmutableDictionary.Create<string, int>(StringComparer.OrdinalIgnoreCase);

    public ImmutableDictionary<string, int> MaxTech { get; init; } =
        ImmutableDictionary.Create<string, int>(StringComparer.OrdinalIgnoreCase);

    public ImmutableDictionary<string, int> Credits { get; init; } =
        ImmutableDictionary.Create<string, int>(StringComparer.OrdinalIgnoreCase);

    public string? Era { get; init; }

    /// <summary>
    ///     Each battle the galaxy has taken an outcome for, by battle key: "won" or "lost". The
    ///     game never takes the other route once a battle has ended, so its listeners for the
    ///     other outcome are moot from here on.
    /// </summary>
    public ImmutableDictionary<string, string> BattleOutcomes { get; init; } =
        ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);

    public StoryWorld WithBattleOutcome(string battleKey, string outcome)
    {
        return this with { BattleOutcomes = BattleOutcomes.SetItem(battleKey, outcome) };
    }

    /// <summary>Counters keyed "name|faction": battlesWon, battlesLost, conquered, built.</summary>
    public ImmutableDictionary<string, int> Counters { get; init; } =
        ImmutableDictionary.Create<string, int>(StringComparer.OrdinalIgnoreCase);

    public ImmutableHashSet<string> Objectives { get; init; } =
        ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);

    public static string UnitKey(string type, string owner, string planet)
    {
        return type + "|" + owner + "|" + planet;
    }

    public int UnitCount(string type, string owner, string planet)
    {
        return Units.GetValueOrDefault(UnitKey(type, owner, planet));
    }

    /// <summary>Total units of a faction, optionally of one type, over every planet.</summary>
    public int UnitsOf(string owner, string? type = null)
    {
        return UnitFacts().Where(u =>
                u.Owner.Equals(owner, StringComparison.OrdinalIgnoreCase) &&
                (type is null || u.Type.Equals(type, StringComparison.OrdinalIgnoreCase)))
            .Sum(u => u.Count);
    }

    public IEnumerable<StoryUnitFact> UnitFacts()
    {
        foreach (var (key, count) in Units)
        {
            if (count <= 0) continue;
            var parts = key.Split('|', 3);
            if (parts.Length == 3) yield return new StoryUnitFact(parts[0], parts[1], parts[2], count);
        }
    }

    public StoryWorld WithPlanet(StoryPlanetFact planet)
    {
        return this with { Planets = Planets.SetItem(planet.Name, planet) };
    }

    public StoryWorld WithPlanetOwner(string planet, string? owner)
    {
        var fact = Planets.GetValueOrDefault(planet) ?? new StoryPlanetFact(planet, null);
        return WithPlanet(fact with { Owner = owner });
    }

    public StoryWorld WithUnits(string type, string owner, string planet, int delta)
    {
        var key = UnitKey(type, owner, planet);
        var next = Math.Max(0, Units.GetValueOrDefault(key) + delta);
        return this with { Units = next == 0 ? Units.Remove(key) : Units.SetItem(key, next) };
    }

    public StoryWorld WithoutUnitType(string type, string? owner = null)
    {
        var units = Units;
        foreach (var fact in UnitFacts())
            if (fact.Type.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                (owner is null || fact.Owner.Equals(owner, StringComparison.OrdinalIgnoreCase)))
                units = units.Remove(UnitKey(fact.Type, fact.Owner, fact.Planet));
        return this with { Units = units };
    }

    public StoryWorld WithCounter(string name, string faction, int delta)
    {
        var key = name + "|" + faction;
        return this with { Counters = Counters.SetItem(key, Counters.GetValueOrDefault(key) + delta) };
    }

    public int Counter(string name, string faction)
    {
        return Counters.GetValueOrDefault(name + "|" + faction);
    }
}

/// <summary>
///     An author's change to the world, or a reward's. <see cref="Kind" /> is one of
///     <see cref="StoryWorldChangeKind" />; the other fields are the arguments that kind reads.
///     <see cref="Faction" /> defaults to the campaign's own faction when null.
/// </summary>
public sealed record StoryWorldChange(string Kind)
{
    /// <summary>On a battle outcome, the battle it resolves - recorded on the world so the other outcome's listeners go moot.</summary>
    public string? BattleKey { get; init; }

    public string? Planet { get; init; }
    public string? UnitType { get; init; }
    public string? Faction { get; init; }

    /// <summary>A GUI button, a generic trigger name, an objective, a mission name.</summary>
    public string? Name { get; init; }

    /// <summary>"space" or "land" for battles; null means either.</summary>
    public string? Mode { get; init; }

    /// <summary>Units built or destroyed, credits added, the tech level set.</summary>
    public int Amount { get; init; } = 1;

    /// <summary>Global flags the change sets as it happens (a tactical script's writes).</summary>
    public IReadOnlyList<StoryFlagWrite>? Flags { get; init; }

    /// <summary>For <see cref="StoryWorldChangeKind.AssumeMet" />: the event whose trigger the author assumes met.</summary>
    public string? NodeId { get; init; }
}

/// <summary>The change kinds. Strings, so they travel the wire as they are.</summary>
public static class StoryWorldChangeKind
{
    public const string CapturePlanet = "capturePlanet";
    public const string BuildUnit = "buildUnit";
    public const string DestroyUnit = "destroyUnit";

    /// <summary>Every unit of a faction (or of one type) is gone: a tactical wipe.</summary>
    public const string DestroyAll = "destroyAll";

    public const string CaptureUnit = "captureUnit";
    public const string SetTech = "setTech";
    public const string AddCredits = "addCredits";
    public const string BattleWon = "battleWon";
    public const string BattleLost = "battleLost";
    public const string BattleStarted = "battleStarted";
    public const string EnterPlanet = "enterPlanet";
    public const string Bounced = "bounced";
    public const string MoveUnit = "moveUnit";
    public const string ClickGui = "clickGui";
    public const string SelectPlanet = "selectPlanet";
    public const string Corrupt = "corrupt";
    public const string BeginEra = "beginEra";
    public const string PlanetDestroyed = "planetDestroyed";
    public const string Generic = "generic";

    /// <summary>No world facet: the author declares the event's trigger condition met.</summary>
    public const string AssumeMet = "assumeMet";
}

/// <summary>
///     Existence of a named game object, the only lookup the simulator makes against the game.
///     A name that is not an emitted symbol is a diagnostic, never a guess.
/// </summary>
public interface IStoryWorldSymbols
{
    bool Exists(string kind, string name);
}

public static class StoryWorldSymbolKind
{
    public const string Planet = "Planet";
    public const string UnitType = "GameObjectType";
    public const string Faction = "Faction";
}