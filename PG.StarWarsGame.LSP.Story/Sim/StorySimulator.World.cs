// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Discovery;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Story.Sim;

/// <summary>
///     The world half of the simulator: the fact table, the author's changes to it, and the
///     dispatch of a change into the events that read that facet. The engine measured 2026-09-19:
///     a game event (planet conquered, unit built, battle won) is a <c>Story_Event</c> dispatch to
///     every active plot, where each listener of that type matches its own parameters and keeps
///     its own count. Nothing here is polled; a change fires what it fires at the moment it is made.
///     Slot positions are the engine's, not the schema's - they are facts about the binary.
/// </summary>
public sealed partial class StorySimulator
{
    // ── Seeding ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     The campaign's starting world in fact form. Planet ownership is inferred from the
    ///     starting forces (the first faction with forces on a planet owns it); the campaign XML
    ///     carries no owner tag. Planets without forces are neutral.
    /// </summary>
    private static StoryWorld SeedWorld(StoryCampaignSeed? seed)
    {
        if (seed is null) return StoryWorld.Empty;
        var world = StoryWorld.Empty;
        foreach (var planet in seed.Planets)
            world = world.WithPlanet(new StoryPlanetFact(planet, null));
        foreach (var force in seed.StartingForces)
        {
            var owner = world.Planets.GetValueOrDefault(force.Planet)?.Owner;
            if (owner is null) world = world.WithPlanetOwner(force.Planet, force.Faction);
            world = world.WithUnits(force.UnitType, force.Faction, force.Planet, 1);
        }

        foreach (var (faction, level) in seed.Tech) world = world with { Tech = world.Tech.SetItem(faction, level) };
        foreach (var (faction, amount) in seed.Credits)
            world = world with { Credits = world.Credits.SetItem(faction, amount) };
        return world;
    }

    // ── Changes ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     Applies an author's change: checks the names exist, writes the facts, then dispatches
    ///     to every active event whose type reads that facet. AssumeMet is the escape hatch for a
    ///     type with no facet and fires the named event like a manual trigger.
    /// </summary>
    public StorySimSnapshot ApplyWorldChange(StorySimSnapshot snapshot, StoryWorldChange change)
    {
        snapshot = RunTransitions(snapshot with { HaltedAt = null });
        if (change.Kind == StoryWorldChangeKind.AssumeMet)
            return change.NodeId is null
                ? Note(snapshot, "", null, StorySimCause.Ignored, "AssumeMet needs an event.")
                : SatisfyTrigger(snapshot, change.NodeId);

        change = change with { Faction = change.Faction ?? _model.Faction };
        var unknown = UnknownName(change);
        if (unknown is not null)
            return Note(snapshot, "", null, StorySimCause.Ignored,
                $"{Describe(change)} ignored - '{unknown}' is not a known name.");

        snapshot = WriteFacts(snapshot, change);
        return Dispatch(snapshot, change, 0);
    }

    /// <summary>
    ///     A battle's end as the galaxy takes it, in the measured order: the outcome dispatches to
    ///     its listeners and is recorded on the world; the summary dialog closes, which first fires
    ///     every active speech-done listener in every running plot and then raises the
    ///     <see cref="BattleEndClosed" /> generic. Speeches the galaxy was still playing were
    ///     killed without callbacks when the battle began, so nothing stays owed for them.
    /// </summary>
    public StorySimSnapshot ResolveBattleOutcome(StorySimSnapshot snapshot, StoryWorldChange outcome)
    {
        snapshot = ApplyWorldChange(snapshot, outcome);
        // The battle is over: the choice is spent and gameplay time runs again.
        if (snapshot.Runtime.World.PendingBattle is not null)
            snapshot = snapshot with
            {
                Runtime = snapshot.Runtime.WithWorld(snapshot.Runtime.World.WithoutPendingBattle())
            };
        var owedSpeeches = snapshot.Runtime.PendingCompletions.Where(k => ParseCompletion(k).Type == SpeechDone)
            .ToList();
        if (owedSpeeches.Count > 0)
            snapshot = snapshot with { Runtime = snapshot.Runtime.WithoutCompletions(owedSpeeches) };
        foreach (var node in _eventNodes)
        {
            if (!IsActive(node, snapshot.Runtime)) continue;
            if (!string.Equals(node.Event!.EventType, SpeechDone, StringComparison.OrdinalIgnoreCase)) continue;
            snapshot = Fire(snapshot, node, StorySimCause.Speech, null, 0);
        }

        return ApplyWorldChange(snapshot,
            new StoryWorldChange(StoryWorldChangeKind.Generic) { Name = BattleEndClosed });
    }

    private string? UnknownName(StoryWorldChange change)
    {
        if (_symbols is null) return null;
        if (change.Planet is { } planet && !_symbols.Exists(StoryWorldSymbolKind.Planet, planet)) return planet;
        if (change.UnitType is { } type && !_symbols.Exists(StoryWorldSymbolKind.UnitType, type)) return type;
        if (change.Faction is { } faction && !_symbols.Exists(StoryWorldSymbolKind.Faction, faction)) return faction;
        return null;
    }

    private static string Describe(StoryWorldChange change)
    {
        var what = change.Kind switch
        {
            StoryWorldChangeKind.CapturePlanet => $"{change.Faction} captures {change.Planet}",
            StoryWorldChangeKind.BuildUnit =>
                $"{change.Faction} builds {change.Amount} x {change.UnitType} at {change.Planet ?? "?"}",
            StoryWorldChangeKind.DestroyUnit => $"{change.Amount} x {change.UnitType} of {change.Faction} destroyed",
            StoryWorldChangeKind.CaptureUnit => $"{change.Faction} captures {change.UnitType}",
            StoryWorldChangeKind.DestroyAll => $"all {change.UnitType ?? "units"} of {change.Faction} destroyed",
            StoryWorldChangeKind.SetTech => $"{change.Faction} tech level {change.Amount}",
            StoryWorldChangeKind.AddCredits => $"{change.Faction} credits {change.Amount:+#;-#;0}",
            StoryWorldChangeKind.BattleWon => $"{change.Faction} wins at {change.Planet ?? "?"}",
            StoryWorldChangeKind.BattleLost => $"{change.Faction} loses at {change.Planet ?? "?"}",
            StoryWorldChangeKind.BattleStarted => $"{change.Mode ?? "a"} battle starts at {change.Planet ?? "?"}",
            StoryWorldChangeKind.EnterPlanet => $"{change.Faction} enters {change.Planet}",
            StoryWorldChangeKind.Bounced => $"{change.Faction} bounced at {change.Planet}",
            StoryWorldChangeKind.MoveUnit => $"{change.UnitType} moves to {change.Planet}",
            StoryWorldChangeKind.ClickGui => $"GUI {change.Name} clicked",
            StoryWorldChangeKind.SelectPlanet => $"{change.Planet} selected",
            StoryWorldChangeKind.Corrupt => $"{change.Planet} corrupted",
            StoryWorldChangeKind.BeginEra => $"era {change.Amount} begins",
            StoryWorldChangeKind.PlanetDestroyed => $"{change.Planet} destroyed",
            StoryWorldChangeKind.Generic => $"generic {change.Name}",
            _ => change.Kind
        };
        return "World: " + what;
    }

    /// <summary>The fact writes of a change. Nothing here reasons about the game; it records what the author said.</summary>
    private static StorySimSnapshot WriteFacts(StorySimSnapshot snapshot, StoryWorldChange change)
    {
        var world = snapshot.Runtime.World;
        var faction = change.Faction!;
        switch (change.Kind)
        {
            case StoryWorldChangeKind.CapturePlanet when change.Planet is { } planet:
                world = world.WithPlanetOwner(planet, faction).WithCounter("conquered", faction, 1);
                break;
            case StoryWorldChangeKind.BuildUnit when change.UnitType is { } type:
                world = world.WithUnits(type, faction, change.Planet ?? "", change.Amount)
                    .WithCounter("built", faction, change.Amount);
                break;
            case StoryWorldChangeKind.DestroyUnit when change.UnitType is { } type:
                world = change.Planet is { } at
                    ? world.WithUnits(type, faction, at, -change.Amount)
                    : RemoveAnywhere(world, type, faction, change.Amount);
                break;
            case StoryWorldChangeKind.CaptureUnit when change.UnitType is { } type:
                world = world.WithUnits(type, faction, change.Planet ?? "", change.Amount);
                break;
            case StoryWorldChangeKind.DestroyAll:
                world = change.UnitType is { } wiped
                    ? world.WithoutUnitType(wiped, faction)
                    : world with
                    {
                        Units = world.Units.RemoveRange(world.UnitFacts()
                            .Where(u => u.Owner.Equals(faction, StringComparison.OrdinalIgnoreCase))
                            .Select(u => StoryWorld.UnitKey(u.Type, u.Owner, u.Planet)))
                    };
                break;
            case StoryWorldChangeKind.SetTech:
                world = world with { Tech = world.Tech.SetItem(faction, change.Amount) };
                break;
            case StoryWorldChangeKind.AddCredits:
                world = world with
                {
                    Credits = world.Credits.SetItem(faction,
                        world.Credits.GetValueOrDefault(faction) + change.Amount)
                };
                break;
            case StoryWorldChangeKind.BattleWon:
                world = world.WithCounter("battlesWon", faction, 1);
                break;
            case StoryWorldChangeKind.BattleLost:
                world = world.WithCounter("battlesLost", faction, 1);
                break;
            case StoryWorldChangeKind.EnterPlanet or StoryWorldChangeKind.MoveUnit
                when change.UnitType is { } type && change.Planet is { } planet:
                world = world.WithUnits(type, faction, planet, change.Amount);
                break;
            case StoryWorldChangeKind.Generic when string.Equals(change.Name,
                StoryGraphBuilder.ContinueTutorialGeneric, StringComparison.OrdinalIgnoreCase):
                // The Continue press closes the tutorial dialog that raised it.
                world = world with { TutorialDialog = null };
                break;
            case StoryWorldChangeKind.Corrupt when change.Planet is { } planet:
                world = world.WithPlanet(
                    (world.Planets.GetValueOrDefault(planet) ?? new StoryPlanetFact(planet, null)) with
                    {
                        Corrupted = true
                    });
                break;
            case StoryWorldChangeKind.BeginEra:
                world = world with { Era = change.Amount.ToString() };
                break;
            // The author's own click on the pending-battle choice: takes the choice, and the
            // click event is raised by the dispatch that follows, as a real click raises it.
            case StoryWorldChangeKind.ClickGui
                when world.PendingBattle is { } pendingBattle && world.PendingBattleChoice is null
                                                              && StoryBattleChoice.OfButton(change.Name) is { } choice:
                world = world.WithPendingBattle(pendingBattle, choice);
                break;
            case StoryWorldChangeKind.PlanetDestroyed when change.Planet is { } planet:
                world = world.WithPlanet(
                    (world.Planets.GetValueOrDefault(planet) ?? new StoryPlanetFact(planet, null)) with
                    {
                        Destroyed = true, Owner = null
                    });
                break;
        }

        if (change.BattleKey is { } battleKey &&
            change.Kind is StoryWorldChangeKind.BattleWon or StoryWorldChangeKind.BattleLost)
            world = world.WithBattleOutcome(battleKey,
                change.Kind == StoryWorldChangeKind.BattleWon ? "won" : "lost");

        var runtime = snapshot.Runtime.WithWorld(world);
        foreach (var write in change.Flags ?? [])
            runtime = runtime.WithFlag(write.Flag, write.Value);
        snapshot = snapshot with { Runtime = runtime };
        var flagText = change.Flags is { Count: > 0 }
            ? " (" + string.Join(", ", change.Flags.Select(f => f.Flag + "=" + f.Value)) + ")"
            : "";
        return Note(snapshot, "", null, StorySimCause.Fact, Describe(change) + flagText + ".");
    }

    private static StoryWorld RemoveAnywhere(StoryWorld world, string type, string faction, int amount)
    {
        foreach (var fact in world.UnitFacts())
        {
            if (amount <= 0) break;
            if (!fact.Type.Equals(type, StringComparison.OrdinalIgnoreCase) ||
                !fact.Owner.Equals(faction, StringComparison.OrdinalIgnoreCase)) continue;
            var take = Math.Min(amount, fact.Count);
            world = world.WithUnits(fact.Type, fact.Owner, fact.Planet, -take);
            amount -= take;
        }

        return world;
    }

    /// <summary>Story_Event: every active listener of the change's facet matches its own parameters.</summary>
    private StorySimSnapshot Dispatch(StorySimSnapshot snapshot, StoryWorldChange change, int depth)
    {
        foreach (var node in _eventNodes)
        {
            if (!IsActive(node, snapshot.Runtime)) continue;
            var verdict = Match(node, change, snapshot.Runtime);
            if (verdict == Verdict.Skip) continue;
            if (verdict == Verdict.Count)
            {
                snapshot = snapshot with { Runtime = snapshot.Runtime.WithHit(node.Id) };
                continue;
            }

            snapshot = snapshot with { Runtime = snapshot.Runtime.WithoutHits(node.Id) };
            snapshot = Fire(snapshot, node, StorySimCause.World, null, depth);
        }

        return snapshot;
    }

    private enum Verdict
    {
        Skip,
        Count,
        Fire
    }

    /// <summary>
    ///     One listener's answer to a change. Counting types answer Count until their threshold,
    ///     which mirrors the engine decrementing a per-event counter on every matching dispatch.
    /// </summary>
    private Verdict Match(StoryNode node, StoryWorldChange change, StoryRuntimeState runtime)
    {
        var storyEvent = node.Event!;
        var type = storyEvent.EventType?.ToUpperInvariant();
        var world = runtime.World;
        var hits = runtime.Hits.GetValueOrDefault(node.Id) + 1;
        var kind = change.Kind;

        bool Counted(int threshold)
        {
            return hits >= Math.Max(1, threshold);
        }

        switch (type)
        {
            case "STORY_CONQUER":
                if (kind != StoryWorldChangeKind.CapturePlanet || !ListAccepts(storyEvent, 0, change.Planet))
                    return Verdict.Skip;
                return Counted(IntParam(storyEvent, 1, 1)) ? Verdict.Fire : Verdict.Count;
            case "STORY_CONQUER_COUNT":
                if (kind != StoryWorldChangeKind.CapturePlanet) return Verdict.Skip;
                return Counted(IntParam(storyEvent, 0, 1)) ? Verdict.Fire : Verdict.Count;
            case "STORY_CONSTRUCT":
                if (kind != StoryWorldChangeKind.BuildUnit || !ListAccepts(storyEvent, 0, change.UnitType))
                    return Verdict.Skip;
                return Counted(IntParam(storyEvent, 1, 1)) ? Verdict.Fire : Verdict.Count;
            case "STORY_TECH_LEVEL":
            case "STORY_BEGIN_ERA":
            {
                var expected = type == "STORY_TECH_LEVEL"
                    ? StoryWorldChangeKind.SetTech
                    : StoryWorldChangeKind.BeginEra;
                if (kind != expected || !IsOwnFaction(change)) return Verdict.Skip;
                // Measured: level >= param fires; a param of -1 fires on the first change instead.
                var level = IntParam(storyEvent, 0, 0);
                return level < 0 || change.Amount >= level ? Verdict.Fire : Verdict.Skip;
            }
            case "STORY_ACCUMULATE":
            {
                if (kind != StoryWorldChangeKind.AddCredits || !IsOwnFaction(change)) return Verdict.Skip;
                // Measured: the constructor's COMPARE_NONE is the switch's GREATER_THAN case.
                var credits = world.Credits.GetValueOrDefault(change.Faction!);
                return Compare(credits, IntParam(storyEvent, 0, 0), StringParam(storyEvent, 1) ?? "GREATER_THAN")
                    ? Verdict.Fire
                    : Verdict.Skip;
            }
            case "STORY_VICTORY":
            {
                if (kind != StoryWorldChangeKind.BattleWon) return Verdict.Skip;
                var faction = StringParam(storyEvent, 0) ?? _model.Faction;
                return faction.Equals(change.Faction, StringComparison.OrdinalIgnoreCase) ? Verdict.Fire : Verdict.Skip;
            }
            case "STORY_MISSION_LOST":
            case "STORY_MISSION_FAILED":
            {
                if (kind != StoryWorldChangeKind.BattleLost) return Verdict.Skip;
                var name = StringParam(storyEvent, 0);
                return name is null || change.Name is null ||
                       name.Equals(change.Name, StringComparison.OrdinalIgnoreCase)
                    ? Verdict.Fire
                    : Verdict.Skip;
            }
            case "STORY_WIN_BATTLES":
            case "STORY_LOSE_BATTLES":
            {
                var expected = type == "STORY_WIN_BATTLES"
                    ? StoryWorldChangeKind.BattleWon
                    : StoryWorldChangeKind.BattleLost;
                if (kind != expected || !ModeAccepts(StringParam(storyEvent, 1), change.Mode)) return Verdict.Skip;
                if (!ListAccepts(storyEvent, 3, change.Planet)) return Verdict.Skip;
                return Counted(IntParam(storyEvent, 0, 1)) ? Verdict.Fire : Verdict.Count;
            }
            case "STORY_SPACE_TACTICAL":
            case "STORY_LAND_TACTICAL":
            {
                if (kind != StoryWorldChangeKind.BattleStarted) return Verdict.Skip;
                var mode = type == "STORY_SPACE_TACTICAL" ? "space" : "land";
                if (change.Mode is not null && !change.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase))
                    return Verdict.Skip;
                return ListAccepts(storyEvent, 1, change.Planet) ? Verdict.Fire : Verdict.Skip;
            }
            case "STORY_LOAD_TACTICAL_MAP":
                return kind == StoryWorldChangeKind.BattleStarted && ListAccepts(storyEvent, 0, change.Planet)
                    ? Verdict.Fire
                    : Verdict.Skip;
            case "STORY_ENTER":
            case "STORY_LAND_ON":
                if (kind != StoryWorldChangeKind.EnterPlanet || !ListAccepts(storyEvent, 0, change.Planet))
                    return Verdict.Skip;
                return change.UnitType is null || ListAccepts(storyEvent, 2, change.UnitType)
                    ? Verdict.Fire
                    : Verdict.Skip;
            case "STORY_FLEET_BOUNCED":
            case "STORY_INVASION_BOUNCED":
                return kind == StoryWorldChangeKind.Bounced && ListAccepts(storyEvent, 0, change.Planet)
                    ? Verdict.Fire
                    : Verdict.Skip;
            case "STORY_PLANET_DESTROYED":
                return kind == StoryWorldChangeKind.PlanetDestroyed && ListAccepts(storyEvent, 0, change.Planet)
                    ? Verdict.Fire
                    : Verdict.Skip;
            case "STORY_MOVE":
            case "STORY_DEPLOY":
                return kind == StoryWorldChangeKind.MoveUnit && ListAccepts(storyEvent, 0, change.UnitType) &&
                       ListAccepts(storyEvent, 1, change.Planet)
                    ? Verdict.Fire
                    : Verdict.Skip;
            case "STORY_DESTROY":
            case "STORY_TACTICAL_DESTROY":
            {
                if (kind != StoryWorldChangeKind.DestroyUnit || !ListAccepts(storyEvent, 0, change.UnitType))
                    return Verdict.Skip;
                if (StringParam(storyEvent, 1) is { } planet && change.Planet is not null &&
                    !planet.Equals(change.Planet, StringComparison.OrdinalIgnoreCase)) return Verdict.Skip;
                return Counted(IntParam(storyEvent, 2, 1)) ? Verdict.Fire : Verdict.Count;
            }
            case "STORY_CHECK_DESTROYED":
            {
                if (kind is not (StoryWorldChangeKind.DestroyUnit or StoryWorldChangeKind.DestroyAll))
                    return Verdict.Skip;
                var faction = StringParam(storyEvent, 0) ?? change.Faction!;
                if (!faction.Equals(change.Faction, StringComparison.OrdinalIgnoreCase)) return Verdict.Skip;
                return world.UnitsOf(faction, StringParam(storyEvent, 2)) == 0 ? Verdict.Fire : Verdict.Skip;
            }
            case "STORY_CAPTURE_STRUCTURE":
            {
                if (kind != StoryWorldChangeKind.CaptureUnit || !ListAccepts(storyEvent, 0, change.UnitType))
                    return Verdict.Skip;
                var faction = StringParam(storyEvent, 1);
                return faction is null || faction.Equals(change.Faction, StringComparison.OrdinalIgnoreCase)
                    ? Verdict.Fire
                    : Verdict.Skip;
            }
            case "STORY_CAPTURE_HERO":
                if (kind != StoryWorldChangeKind.CaptureUnit || !ListAccepts(storyEvent, 0, change.UnitType))
                    return Verdict.Skip;
                return Counted(IntParam(storyEvent, 1, 1)) ? Verdict.Fire : Verdict.Count;
            case "STORY_DEFEAT_HERO":
                if (kind != StoryWorldChangeKind.DestroyUnit || !ListAccepts(storyEvent, 0, change.UnitType))
                    return Verdict.Skip;
                return Counted(IntParam(storyEvent, 1, 1)) ? Verdict.Fire : Verdict.Count;
            case "STORY_SELECT_PLANET":
                return kind == StoryWorldChangeKind.SelectPlanet && ListAccepts(storyEvent, 0, change.Planet)
                    ? Verdict.Fire
                    : Verdict.Skip;
            case "STORY_CLICK_GUI":
                return kind == StoryWorldChangeKind.ClickGui && NameAccepts(storyEvent, 0, change.Name)
                    ? Verdict.Fire
                    : Verdict.Skip;
            case "STORY_GENERIC":
            case "STORY_BUY_BLACK_MARKET":
            case "STORY_GALACTIC_SABOTAGE":
                return kind == StoryWorldChangeKind.Generic && ListAccepts(storyEvent, 0, change.Name)
                    ? Verdict.Fire
                    : Verdict.Skip;
            case "STORY_CORRUPTION_CHANGED":
            case "STORY_CORRUPTION_INCREASED":
            case "STORY_OPEN_CORRUPTION":
                return kind == StoryWorldChangeKind.Corrupt && ListAccepts(storyEvent, 0, change.Planet)
                    ? Verdict.Fire
                    : Verdict.Skip;
            default:
                return Verdict.Skip;
        }
    }

    /// <summary>The world facet of an event type, its candidates, and the first candidate as a ready change.</summary>
    private (string? Facet, IReadOnlyList<string> Options, StoryWorldChange? Suggested) FacetOf(StoryEvent storyEvent)
    {
        var type = storyEvent.EventType?.ToUpperInvariant();
        var own = _model.Faction;
        switch (type)
        {
            case "STORY_CONQUER":
                return Planets(StoryWorldChangeKind.CapturePlanet, storyEvent, 0, own);
            case "STORY_CONQUER_COUNT":
                return (StoryWorldChangeKind.CapturePlanet, [],
                    new StoryWorldChange(StoryWorldChangeKind.CapturePlanet) { Faction = own });
            case "STORY_CONSTRUCT":
                return Types(StoryWorldChangeKind.BuildUnit, storyEvent, 0, own);
            case "STORY_TECH_LEVEL":
                return (StoryWorldChangeKind.SetTech, [],
                    new StoryWorldChange(StoryWorldChangeKind.SetTech)
                        { Faction = own, Amount = Math.Max(1, IntParam(storyEvent, 0, 1)) });
            case "STORY_BEGIN_ERA":
                return (StoryWorldChangeKind.BeginEra, [],
                    new StoryWorldChange(StoryWorldChangeKind.BeginEra)
                        { Faction = own, Amount = Math.Max(1, IntParam(storyEvent, 0, 1)) });
            case "STORY_ACCUMULATE":
                return (StoryWorldChangeKind.AddCredits, [],
                    new StoryWorldChange(StoryWorldChangeKind.AddCredits)
                        { Faction = own, Amount = IntParam(storyEvent, 0, 0) + 1 });
            case "STORY_VICTORY":
                return (StoryWorldChangeKind.BattleWon, [],
                    new StoryWorldChange(StoryWorldChangeKind.BattleWon)
                        { Faction = StringParam(storyEvent, 0) ?? own });
            case "STORY_MISSION_LOST":
            case "STORY_MISSION_FAILED":
                return (StoryWorldChangeKind.BattleLost, [],
                    new StoryWorldChange(StoryWorldChangeKind.BattleLost)
                        { Faction = own, Name = StringParam(storyEvent, 0) });
            case "STORY_WIN_BATTLES":
                return Planets(StoryWorldChangeKind.BattleWon, storyEvent, 3, own, ModeOf(StringParam(storyEvent, 1)));
            case "STORY_LOSE_BATTLES":
                return Planets(StoryWorldChangeKind.BattleLost, storyEvent, 3, own, ModeOf(StringParam(storyEvent, 1)));
            case "STORY_SPACE_TACTICAL":
                return Planets(StoryWorldChangeKind.BattleStarted, storyEvent, 1, own, "space");
            case "STORY_LAND_TACTICAL":
                return Planets(StoryWorldChangeKind.BattleStarted, storyEvent, 1, own, "land");
            case "STORY_LOAD_TACTICAL_MAP":
                return Planets(StoryWorldChangeKind.BattleStarted, storyEvent, 0, own);
            case "STORY_ENTER":
            case "STORY_LAND_ON":
                return Planets(StoryWorldChangeKind.EnterPlanet, storyEvent, 0, own);
            case "STORY_FLEET_BOUNCED":
            case "STORY_INVASION_BOUNCED":
                return Planets(StoryWorldChangeKind.Bounced, storyEvent, 0, own);
            case "STORY_PLANET_DESTROYED":
                return Planets(StoryWorldChangeKind.PlanetDestroyed, storyEvent, 0, own);
            case "STORY_MOVE":
            case "STORY_DEPLOY":
            {
                var (facet, options, suggested) = Types(StoryWorldChangeKind.MoveUnit, storyEvent, 0, own);
                var planet = ListParam(storyEvent, 1).FirstOrDefault();
                return (facet, options, suggested is null ? null : suggested with { Planet = planet });
            }
            case "STORY_DESTROY":
            case "STORY_TACTICAL_DESTROY":
            case "STORY_DEFEAT_HERO":
            {
                var (facet, options, suggested) = Types(StoryWorldChangeKind.DestroyUnit, storyEvent, 0, null);
                var planet = type == "STORY_DEFEAT_HERO" ? null : StringParam(storyEvent, 1);
                return (facet, options, suggested is null ? null : suggested with { Planet = planet });
            }
            case "STORY_CHECK_DESTROYED":
                return (StoryWorldChangeKind.DestroyAll, [], new StoryWorldChange(StoryWorldChangeKind.DestroyAll)
                {
                    Faction = StringParam(storyEvent, 0) ?? own, UnitType = StringParam(storyEvent, 2)
                });
            case "STORY_CAPTURE_STRUCTURE":
            case "STORY_CAPTURE_HERO":
                return Types(StoryWorldChangeKind.CaptureUnit, storyEvent, 0, own);
            case "STORY_SELECT_PLANET":
                return Planets(StoryWorldChangeKind.SelectPlanet, storyEvent, 0, own);
            case "STORY_CLICK_GUI":
            {
                var name = StringParam(storyEvent, 0);
                return (StoryWorldChangeKind.ClickGui, ListParam(storyEvent, 0),
                    name is null ? null : new StoryWorldChange(StoryWorldChangeKind.ClickGui) { Name = name });
            }
            case "STORY_GENERIC":
            case "STORY_BUY_BLACK_MARKET":
            case "STORY_GALACTIC_SABOTAGE":
            {
                var options = ListParam(storyEvent, 0);
                return (StoryWorldChangeKind.Generic, options,
                    options.Count == 0
                        ? null
                        : new StoryWorldChange(StoryWorldChangeKind.Generic) { Name = options[0] });
            }
            case "STORY_CORRUPTION_CHANGED":
            case "STORY_CORRUPTION_INCREASED":
            case "STORY_OPEN_CORRUPTION":
                return Planets(StoryWorldChangeKind.Corrupt, storyEvent, 0, own);
            default:
                return (null, [], null);
        }
    }

    // An event that names no candidate (any planet, any unit) gets a facet but no suggestion: the
    // author has to say which, and a change without its subject would be a guess.
    private static (string, IReadOnlyList<string>, StoryWorldChange?) Planets(string kind, StoryEvent storyEvent,
        int slot, string faction, string? mode = null)
    {
        var options = ListParam(storyEvent, slot);
        return (kind, options, options.Count == 0
            ? null
            : new StoryWorldChange(kind) { Planet = options[0], Faction = faction, Mode = mode });
    }

    private static (string, IReadOnlyList<string>, StoryWorldChange?) Types(string kind, StoryEvent storyEvent,
        int slot, string? faction)
    {
        var options = ListParam(storyEvent, slot);
        return (kind, options, options.Count == 0
            ? null
            : new StoryWorldChange(kind) { UnitType = options[0], Faction = faction });
    }

    // ── Reward writes to the world ───────────────────────────────────────────

    /// <summary>
    ///     Rewards that change a fact write it; SET_TECH_LEVEL and CREDITS also dispatch, since the
    ///     game raises the matching event when they change. Rewards whose effect is game logic
    ///     (an invasion, a fleet move, an AI trigger) are noted with no effect.
    /// </summary>
    private StorySimSnapshot ApplyWorldReward(StorySimSnapshot snapshot, StoryNode node, string upperReward, int depth)
    {
        var storyEvent = node.Event!;
        var world = snapshot.Runtime.World;
        var own = _model.Faction;
        switch (upperReward)
        {
            case "LINK_TACTICAL":
                return LinkTactical(snapshot, node);
            case "TUTORIAL_DIALOG" when Param(storyEvent, 0) is { } text:
                // The dialog is up until its Continue button raises Continue_Tutorial; that
                // generic is answerable only meanwhile (StoryWorld.TutorialDialog).
                return Fact(snapshot, node, world with { TutorialDialog = text },
                    $"  -> tutorial dialog {text} up - Continue raises {StoryGraphBuilder.ContinueTutorialGeneric}.");
            case "FORCE_CLICK_GUI" when Param(storyEvent, 0) is { } component:
            {
                // Measured: the reward presses the named command-bar component's release handler
                // directly; the story's click event is raised by the command bar's own mouse
                // handling alone, so no listener hears this press. The pending-battle buttons
                // are the one press with a story-side effect.
                var choice = StoryBattleChoice.OfButton(component);
                if (choice is not null && world.PendingBattle is { } pending && world.PendingBattleChoice is null)
                    return Fact(snapshot, node, world.WithPendingBattle(pending, choice),
                        choice == StoryBattleChoice.Fight
                            ? $"  -> {component} pressed: the battle {pending} begins."
                            : $"  -> {component} pressed: {pending} is auto-resolved - decide its outcome.");
                return Note(snapshot, node.Id, null, StorySimCause.Ignored,
                    $"  -> FORCE_CLICK_GUI {component}: pressed in the game; no listener hears a forced click.");
            }
            case "PLANET_FACTION" when Param(storyEvent, 0) is { } planet && Param(storyEvent, 1) is { } faction:
                return Fact(snapshot, node, world.WithPlanetOwner(planet, faction),
                    $"  -> {planet} now belongs to {faction}.");
            case "CHANGE_OWNER" when Param(storyEvent, 0) is { } type && Param(storyEvent, 1) is { } faction:
            {
                var next = world;
                foreach (var fact in world.UnitFacts()
                             .Where(u => u.Type.Equals(type, StringComparison.OrdinalIgnoreCase)))
                    next = next.WithUnits(fact.Type, fact.Owner, fact.Planet, -fact.Count)
                        .WithUnits(fact.Type, faction, fact.Planet, fact.Count);
                return Fact(snapshot, node, next, $"  -> {type} now belongs to {faction}.");
            }
            case "SPAWN_HERO" when Param(storyEvent, 0) is { } type:
                return Fact(snapshot, node, world.WithUnits(type, own, Param(storyEvent, 1) ?? "", 1),
                    $"  -> {type} spawned at {Param(storyEvent, 1) ?? "?"}.");
            case "UNIQUE_UNIT" when Param(storyEvent, 0) is { } type:
            {
                var count = Math.Max(1, ParseInt(Param(storyEvent, 2)));
                return Fact(snapshot, node, world.WithUnits(type, own, Param(storyEvent, 1) ?? "", count),
                    $"  -> {count} x {type} at {Param(storyEvent, 1) ?? "?"}.");
            }
            case "REMOVE_UNIT" or "DESTROY_OBJECT" when Param(storyEvent, 0) is { } type:
                return Fact(snapshot, node, world.WithoutUnitType(type), $"  -> {type} removed.");
            case "SET_TECH_LEVEL" when Param(storyEvent, 0) is { } faction:
            {
                var change = new StoryWorldChange(StoryWorldChangeKind.SetTech)
                    { Faction = faction, Amount = ParseInt(Param(storyEvent, 1)) };
                return Dispatch(WriteFacts(snapshot, change), change, depth + 1);
            }
            case "SET_MAX_TECH_LEVEL" when Param(storyEvent, 0) is { } faction:
                return Fact(snapshot, node,
                    world with { MaxTech = world.MaxTech.SetItem(faction, ParseInt(Param(storyEvent, 1))) },
                    $"  -> {faction} max tech {ParseInt(Param(storyEvent, 1))}.");
            case "CREDITS":
            {
                var change = new StoryWorldChange(StoryWorldChangeKind.AddCredits)
                    { Faction = own, Amount = ParseInt(Param(storyEvent, 0)) };
                return Dispatch(WriteFacts(snapshot, change), change, depth + 1);
            }
            case "ADD_OBJECTIVE" when Param(storyEvent, 0) is { } objective:
                return Fact(snapshot, node, world with { Objectives = world.Objectives.Add(objective) },
                    $"  -> objective {objective} added.");
            case "OBJECTIVE_COMPLETE" or "OBJECTIVE_FAILED" or "REMOVE_OBJECTIVE"
                when Param(storyEvent, 0) is { } objective:
                return Fact(snapshot, node, world with { Objectives = world.Objectives.Remove(objective) },
                    $"  -> objective {objective} {upperReward.ToLowerInvariant().Replace("objective_", "").Replace("remove_objective", "removed")}.");
            case "REMOVE_ALL_OBJECTIVES":
                return Fact(snapshot, node, world with { Objectives = world.Objectives.Clear() },
                    "  -> all objectives removed.");
            case "INVADE_PLANET" or "MOVE_FLEET" or "TRIGGER_AI" or "FORCE_RETREAT" or "SET_PLANET_SPAWN":
                return Note(snapshot, node.Id, null, StorySimCause.Ignored,
                    $"  -> {upperReward}: the game would act here; the simulator records no fact for it.");
            default:
                return snapshot;
        }
    }

    /// <summary>
    ///     LINK_TACTICAL, measured: the reward queues the next tactical conflict and, from the
    ///     galaxy, transitions to it at once - with the pending-battle choice up unless parameter
    ///     13 turns it off, in which case the battle begins straight away. The engine refuses a
    ///     second queue while one is pending, and queues nothing. From inside a battle the queue
    ///     waits for the galaxy, which the sub-graph does not model.
    /// </summary>
    private StorySimSnapshot LinkTactical(StorySimSnapshot snapshot, StoryNode node)
    {
        var world = snapshot.Runtime.World;
        var stub = _model.Graph.Edges
            .FirstOrDefault(e => e.Kind == StoryEdgeKind.Tactical && e.FromId == node.Id)?.ToId;
        var battleKey = stub is null
            ? null
            : _model.Battles.FirstOrDefault(b => StoryGraphScoper.TacticalNodeId(b.Key) == stub)?.Key;
        if (battleKey is null)
            return Note(snapshot, node.Id, null, StorySimCause.Ignored,
                "  -> LINK_TACTICAL names no battle the model knows.");
        if (Scope is not null)
            return Note(snapshot, node.Id, null, StorySimCause.Ignored,
                $"  -> LINK_TACTICAL {battleKey} from inside a battle: queued for the galaxy, not modelled here.");
        if (world.PendingBattle is { } pending)
            return Note(snapshot, node.Id, null, StorySimCause.Ignored,
                $"  -> LINK_TACTICAL {battleKey} while {pending} is pending: the game queues nothing.");
        // Linked again after an outcome - the tutorial resets its branch on a loss and links the
        // same mission once more: a new attempt, with no outcome on the books.
        if (world.BattleOutcomes.ContainsKey(battleKey))
            world = world with { BattleOutcomes = world.BattleOutcomes.Remove(battleKey) };

        var showChoice = Param(node.Event!, 12) is not { } raw || ParseInt(raw) != 0;
        return showChoice
            ? Fact(snapshot, node, world.WithPendingBattle(battleKey),
                $"  -> battle {battleKey} pending: fight or auto-resolve.")
            : Fact(snapshot, node, world.WithPendingBattle(battleKey, StoryBattleChoice.Fight),
                $"  -> battle {battleKey} begins.");
    }

    private static StorySimSnapshot Fact(StorySimSnapshot snapshot, StoryNode node, StoryWorld world, string detail)
    {
        return Note(snapshot with { Runtime = snapshot.Runtime.WithWorld(world) }, node.Id, null, StorySimCause.Fact,
            detail);
    }

    // ── Parameter helpers ────────────────────────────────────────────────────

    private bool IsOwnFaction(StoryWorldChange change)
    {
        return change.Faction is null || change.Faction.Equals(_model.Faction, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ListParam(StoryEvent storyEvent, int slot)
    {
        var raw = storyEvent.EventParams.FirstOrDefault(p => p.Position == slot)?.RawValue;
        return raw is null ? [] : StoryReferenceTypes.SplitList(raw).ToList();
    }

    private static string? StringParam(StoryEvent storyEvent, int slot)
    {
        var raw = storyEvent.EventParams.FirstOrDefault(p => p.Position == slot)?.RawValue.Trim();
        return string.IsNullOrEmpty(raw) ? null : raw;
    }

    private static int IntParam(StoryEvent storyEvent, int slot, int fallback)
    {
        var raw = StringParam(storyEvent, slot);
        return raw is null ? fallback : ParseInt(raw);
    }

    /// <summary>An empty list accepts anything (measured for the planet lists); a value must be listed.</summary>
    private static bool ListAccepts(StoryEvent storyEvent, int slot, string? value)
    {
        var list = ListParam(storyEvent, slot);
        if (list.Count == 0) return true;
        return value is not null && list.Any(v => v.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool NameAccepts(StoryEvent storyEvent, int slot, string? value)
    {
        var name = StringParam(storyEvent, slot);
        return name is null || (value is not null && name.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ModeAccepts(string? filter, string? mode)
    {
        if (filter is null || mode is null) return true;
        var upper = filter.ToUpperInvariant();
        if (upper.Contains("SPACE")) return mode.Equals("space", StringComparison.OrdinalIgnoreCase);
        if (upper.Contains("LAND")) return mode.Equals("land", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static string? ModeOf(string? filter)
    {
        if (filter is null) return null;
        var upper = filter.ToUpperInvariant();
        if (upper.Contains("SPACE")) return "space";
        if (upper.Contains("LAND")) return "land";
        return null;
    }

    private static bool Compare(int value, int target, string op)
    {
        return op.ToUpperInvariant() switch
        {
            "EQUAL_TO" => value == target,
            "LESS_THAN" => value < target,
            "GREATER_THAN_EQUAL_TO" => value >= target,
            "LESS_THAN_EQUAL_TO" => value <= target,
            "NOT_EQUAL_TO" => false,
            _ => value > target
        };
    }
}