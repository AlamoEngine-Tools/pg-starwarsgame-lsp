// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     Every occurrence of a tag an object declares more than once.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="EffectiveObjectResolver" /> cannot answer this. It accumulates into a dictionary
///         keyed by tag name, and only the <c>variantMode: merge</c> plus <c>multipleAllowed</c>
///         branch keeps an occurrence list - every other repeated tag collapses to the last one seen.
///         Exactly one tag in the whole schema (<c>Death_Clone</c>) sets that mode, so in practice
///         resolving GameConstants yields ONE <c>Damage_To_Armor_Mod</c> out of 2426.
///     </para>
///     <para>
///         That collapse is correct for variant merging and simply irrelevant to a singleton, which
///         has no base chain to merge with. The tag SOURCES keep every occurrence, so this reads from
///         them directly - layered the same way the resolver layers, workspace shadowing baseline per
///         object, so a mod shipping its own <c>GameConstants.xml</c> replaces the base game's list
///         rather than appending to it.
///     </para>
/// </remarks>
public static class RepeatedTagReader
{
    /// <summary>
    ///     Every value <paramref name="objectId" /> declares for <paramref name="tagName" />, in
    ///     document order. Empty when the object or the tag is absent.
    /// </summary>
    public static IReadOnlyList<string> Values(
        GameIndex index, IVariantTagSource workspaceSource, string objectId, string tagName)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(workspaceSource);

        return Tags(index, workspaceSource, objectId)
            .Where(tag => tag.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase))
            .Select(tag => tag.Value.Trim())
            .ToList();
    }

    /// <summary>
    ///     Every occurrence of <paramref name="tagName" /> split on commas, for the
    ///     <c>&lt;Tag&gt; key, value &lt;/Tag&gt;</c> shape GameConstants uses for its lookup tables.
    /// </summary>
    /// <remarks>
    ///     Rows whose field count does not match <paramref name="fields" /> are dropped rather than
    ///     padded: a half-written row means something different from a row with a blank field, and
    ///     silently inventing the missing one would hide the authoring mistake.
    /// </remarks>
    public static IReadOnlyList<string[]> Rows(
        GameIndex index, IVariantTagSource workspaceSource, string objectId, string tagName,
        int fields)
    {
        return Values(index, workspaceSource, objectId, tagName)
            .Select(value => value.Split(',', StringSplitOptions.TrimEntries))
            .Where(row => row.Length == fields)
            .ToList();
    }

    /// <summary>
    ///     The object's tags from whichever layer knows it - workspace first, exactly as
    ///     <c>EffectiveObjectResolver.TagsFor</c> does it.
    /// </summary>
    private static IEnumerable<VariantTag> Tags(
        GameIndex index, IVariantTagSource workspaceSource, string objectId)
    {
        if (workspaceSource.TryGetTags(objectId) is { } workspace)
            return workspace;

        return index.Baseline.ObjectTags.TryGetValue(objectId, out var baseline)
            ? baseline.Select(t => new VariantTag(t.TagName, t.Value, t.Fragment, t.StartLine))
            : [];
    }
}
