// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Story.Discovery;

/// <summary>One <c>Starting_Forces</c> line: "Faction, Planet, Unit".</summary>
public sealed record StoryStartingForce(string Faction, string Planet, string UnitType);

/// <summary>
///     What a campaign declares about its starting world, in the simplified form the simulator
///     seeds its fact table from: the planet list, starting forces as presence lines, starting
///     tech and credits per faction, and home planets. Read once at scan time; the simulator
///     never re-derives anything from the game after that.
/// </summary>
public sealed record StoryCampaignSeed(
    IReadOnlyList<string> Planets,
    IReadOnlyList<StoryStartingForce> StartingForces,
    IReadOnlyDictionary<string, int> Tech,
    IReadOnlyDictionary<string, int> Credits,
    IReadOnlyDictionary<string, string> HomePlanets)
{
    public static readonly StoryCampaignSeed Empty = new([], [],
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}
