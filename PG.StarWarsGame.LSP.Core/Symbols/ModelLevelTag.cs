// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     The <c>_ALT&lt;n&gt;</c> and <c>_LOD&lt;n&gt;</c> tags a model encodes in its mesh, bone and
///     proxy NAMES - the only place either level is written down.
/// </summary>
/// <remarks>
///     <para>
///         One definition, because three parts of the server need it and a drifting copy is a
///         silent wrong answer: the ALO reader recovers the levels a mesh or proxy belongs to, the
///         preview's particle endpoint strips them to reach the asset a proxy names, and both the
///         preview scene and the XML validator ask which stages a model tags at all.
///     </para>
///     <para>
///         The rule is the engine's, from <c>RenderObject.cpp</c>'s <c>ParseName</c>: find the
///         marker, read the digits that follow it, and treat a marker with NO digits after it as
///         part of the name. <c>p_alterac</c> and <c>p_lodestone</c> are not tagged.
///     </para>
/// </remarks>
public static class ModelLevelTag
{
    private const string AltMarker = "_ALT";
    private const string LodMarker = "_LOD";

    /// <summary>The damage stage this name declares, or null when it declares none.</summary>
    public static int? AltOf(string? name)
    {
        return LevelAfter(name, AltMarker);
    }

    /// <summary>The detail level this name declares, or null when it declares none.</summary>
    public static int? LodOf(string? name)
    {
        return LevelAfter(name, LodMarker);
    }

    /// <summary>
    ///     Every damage stage tagged anywhere in <paramref name="names" />, ascending.
    /// </summary>
    /// <remarks>
    ///     Fed the model's bone and mesh names - what <see cref="GameIndex.ModelBones" /> holds.
    ///     Measured across the 1957 shipped models: of the 149 carrying an ALT-tagged proxy, not one
    ///     tags a level that no bone or mesh also tags, so that catalogue sees the whole of what a
    ///     model stages even though it holds no proxy names.
    /// </remarks>
    public static IReadOnlySet<int> AltLevelsIn(IEnumerable<string>? names)
    {
        var levels = new SortedSet<int>();
        if (names is null)
            return levels;

        foreach (var name in names)
            if (AltOf(name) is { } level)
                levels.Add(level);

        return levels;
    }

    /// <summary>
    ///     The declared damage stages that nothing in <paramref name="tagged" /> is tagged for,
    ///     ascending. Empty when the model covers everything the object asks of it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ONE direction. A model may tag more stages than the XML uses and that is never
    ///         reported - it is an asset carrying more than this object asks of it, the same shape as
    ///         a <c>PTE_</c> effect on a unit with no TURBO or a stealth shell on a unit with no
    ///         cloak. The reverse is a unit that reaches a damage state and does not change.
    ///     </para>
    ///     <para>
    ///         <b>Stage 0 is never asked for.</b> It is the undamaged state a model opens in, and
    ///         every untagged mesh in the file IS it, so requiring an <c>_ALT0</c> would fire on
    ///         almost every object that declares the table at all.
    ///     </para>
    ///     <para>
    ///         Measured across foc: 219 objects declare <c>Land_Damage_Alternates</c>, 212 have every
    ///         stage tagged, one names no model, and 6 declare a stage the model has nothing for.
    ///         Counting the tables kept inside XML COMMENTS - the mineral processors and
    ///         <c>Dark_Trooper_PhaseIII</c> among them - is what made a first pass of that
    ///         measurement say 222 and 9.
    ///     </para>
    /// </remarks>
    public static IReadOnlyList<int> StagesNotTagged(
        IEnumerable<int>? declared, IReadOnlySet<int> tagged)
    {
        ArgumentNullException.ThrowIfNull(tagged);

        if (declared is null)
            return [];

        return [.. new SortedSet<int>(declared.Where(stage => stage > 0 && !tagged.Contains(stage)))];
    }

    /// <summary>
    ///     The name with every level tag removed, which is what a proxy's ASSET is filed under.
    /// </summary>
    /// <remarks>
    ///     Every occurrence, either marker, in either order - <c>p_fire_small01_ALT3_LOD0</c> is a
    ///     real shape - matching the loop <c>ObjectTemplate.cpp</c> runs before
    ///     <c>Assets::LoadParticleSystem</c>.
    /// </remarks>
    public static string Strip(string? name)
    {
        return StripMarker(StripMarker(name ?? string.Empty, AltMarker), LodMarker);
    }

    /// <summary>
    ///     The number after the FIRST occurrence of the marker, or null.
    /// </summary>
    /// <remarks>
    ///     First occurrence only, deliberately: <c>ParseName</c> takes one <c>find</c> and gives up
    ///     if what follows is not numeric, rather than hunting for a later tagged one. Matching that
    ///     matters more than being clever - the engine is what decides which level a mesh is on.
    /// </remarks>
    private static int? LevelAfter(string? name, string marker)
    {
        if (name is null)
            return null;

        var at = name.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return null;

        var start = at + marker.Length;
        var end = start;
        while (end < name.Length && char.IsAsciiDigit(name[end]))
            end++;

        if (end == start)
            return null;

        return int.TryParse(name.AsSpan(start, end - start), out var value)
            ? Math.Max(0, value)
            : null;
    }

    private static string StripMarker(string value, string marker)
    {
        var from = 0;

        while (true)
        {
            var at = value.IndexOf(marker, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return value;

            var end = at + marker.Length;
            while (end < value.Length && char.IsAsciiDigit(value[end]))
                end++;

            if (end == at + marker.Length)
            {
                // No digits, so this reads like a tag without being one - `p_alterac`.
                from = at + marker.Length;
                continue;
            }

            value = value[..at] + value[end..];
            from = at;
        }
    }
}
