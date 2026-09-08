// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     The icons one hardpoint type shows in each of its targeting states.
/// </summary>
/// <remarks>
///     Seven states, and they are not all distinct: on every shipped type the disabled pair reuses
///     the plain and tracked art, so <c>Engines</c> resolves to three files rather than seven. Kept
///     as seven fields anyway, because a mod is free to give them separate art and collapsing them
///     here would silently ignore that.
/// </remarks>
public sealed record PreviewReticleStates(
    string? Enemy,
    string? EnemyTracked,
    string? Friendly,
    string? FriendlyTracked,
    string? FriendlyRepairing,
    string? FriendlyDisabled,
    string? FriendlyDisabledTracked);

/// <summary>
///     What the game draws over a targetable hardpoint, read from GameConstants.
/// </summary>
/// <remarks>
///     <para>
///         Seven repeated tags of the form
///         <c>&lt;Tag&gt; HARD_POINT_TYPE, I_Icon_Name &lt;/Tag&gt;</c>, one row per type - 91 rows
///         over 13 types in foc, 77 over 11 in eaw. Every type used by a shipped hardpoint has a
///         mapping in both trees.
///     </para>
///     <para>
///         Read through <see cref="RepeatedTagReader" /> rather than the effective-object resolver,
///         which collapses a repeated tag to its last occurrence and would leave exactly one of the
///         91 rows.
///     </para>
/// </remarks>
public sealed class PreviewReticleMap
{
    /// <summary>The tag for each state, in the order <see cref="PreviewReticleStates" /> holds them.</summary>
    private static readonly string[] StateTags =
    [
        "HardPoint_Target_Reticle_Enemy_Texture",
        "HardPoint_Target_Reticle_Enemy_Tracked_Texture",
        "HardPoint_Target_Reticle_Friendly_Texture",
        "HardPoint_Target_Reticle_Friendly_Tracked_Texture",
        "HardPoint_Target_Reticle_Friendly_Repairing_Texture",
        "HardPoint_Target_Reticle_Friendly_Disabled_Texture",
        "HardPoint_Target_Reticle_Friendly_Disabled_Tracked_Texture"
    ];

    private readonly Dictionary<string, string?[]> _byType;

    private PreviewReticleMap(
        Dictionary<string, string?[]> byType, float? enemySize, float? friendlySize)
    {
        _byType = byType;
        EnemyScreenSize = enemySize;
        FriendlyScreenSize = friendlySize;
    }

    /// <summary>
    ///     How large the reticle is drawn, as the game writes it - <c>0.03</c> in both shipped trees.
    /// </summary>
    /// <remarks>
    ///     What it is a fraction OF is not settled; screen height is the obvious reading and the
    ///     preview confirms it by looking right, not by argument.
    /// </remarks>
    public float? EnemyScreenSize { get; }

    /// <inheritdoc cref="EnemyScreenSize" />
    public float? FriendlyScreenSize { get; }

    /// <summary>Every distinct icon named by any state of any type. Five families in the base game.</summary>
    public IReadOnlyCollection<string> IconNames =>
        _byType.Values
            .SelectMany(states => states)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The states for a hardpoint type, or null when GameConstants maps none.</summary>
    public PreviewReticleStates? ForType(string? hardpointType)
    {
        if (string.IsNullOrEmpty(hardpointType) || !_byType.TryGetValue(hardpointType, out var s))
            return null;

        return new PreviewReticleStates(s[0], s[1], s[2], s[3], s[4], s[5], s[6]);
    }

    /// <summary>Reads the map out of the GameConstants singleton.</summary>
    public static PreviewReticleMap From(GameIndex index, IVariantTagSource workspaceSource)
    {
        var byType = new Dictionary<string, string?[]>(StringComparer.OrdinalIgnoreCase);

        for (var state = 0; state < StateTags.Length; state++)
            foreach (var row in RepeatedTagReader.Rows(
                         index, workspaceSource, EncyclopediaTags.GameConstantsId, StateTags[state], 2))
            {
                if (row[0].Length == 0 || row[1].Length == 0)
                    continue;

                if (!byType.TryGetValue(row[0], out var states))
                    byType[row[0]] = states = new string?[StateTags.Length];

                states[state] = row[1];
            }

        return new PreviewReticleMap(byType,
            Single(index, workspaceSource, "HardPoint_Target_Reticle_Enemy_Screen_Size"),
            Single(index, workspaceSource, "HardPoint_Target_Reticle_Friendly_Screen_Size"));
    }

    private static float? Single(GameIndex index, IVariantTagSource source, string tagName)
    {
        var values = RepeatedTagReader.Values(
            index, source, EncyclopediaTags.GameConstantsId, tagName);

        return values.Count > 0 &&
               float.TryParse(values[0], System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
