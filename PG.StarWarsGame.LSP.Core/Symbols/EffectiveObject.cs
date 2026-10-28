// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     The result of merging an object with its variant base chain. When <see cref="Found" /> is false the
///     object id is not in the index. When <see cref="Cyclic" /> is true the base chain loops back on
///     itself at <see cref="CycleObjectId" />; <see cref="Tags" /> then holds the partial merge up to the loop.
/// </summary>
/// <remarks>
///     A tag's value is read through the accessors, never off <see cref="Tags" /> directly: they
///     agree on which occurrence counts, on trimming and on how a number or a flag is read, where
///     the preview, the encyclopedia and the validation handlers each used to carry a reader of
///     their own. <see cref="Tags" /> itself is for the callers that want every occurrence or an
///     entry's provenance.
/// </remarks>
/// <param name="ObjectId">The canonical id of the resolved object.</param>
/// <param name="TypeName">The resolved object's GameObject type, or null.</param>
/// <param name="Found">Whether the object id resolved to a symbol.</param>
/// <param name="Cyclic">Whether a cycle was detected while walking the base chain.</param>
/// <param name="CycleObjectId">The id at which the chain looped, or null.</param>
/// <param name="Chain">The base chain from most-derived (this object) to the root base.</param>
/// <param name="Tags">The effective tag set, in first-seen order.</param>
public sealed record EffectiveObject(
    string ObjectId,
    string? TypeName,
    bool Found,
    bool Cyclic,
    string? CycleObjectId,
    ImmutableArray<string> Chain,
    ImmutableArray<EffectiveTag> Tags
)
{
    /// <summary>
    ///     The trimmed value of a scalar tag, or null when the object does not carry one. Tag names
    ///     match case-insensitively, and a repeated tag gives its last value - the write the engine
    ///     keeps. An empty element writes no value, so it reads as the tag not being there.
    /// </summary>
    public string? ValueOf(string tagName)
    {
        var value = Last(tagName)?.Value.Trim();

        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    ///     The number the game reads from a float tag - the number at the front of the value, whatever
    ///     follows it - or null when the tag is absent or no number starts it. Null rather than 0: a
    ///     cone of 0 degrees and a hardpoint that declares no cone are different things.
    /// </summary>
    public float? FloatOf(string tagName)
    {
        return EngineNumber.ReadFloat(ValueOf(tagName));
    }

    /// <summary>
    ///     The whole number the game reads from an integer tag, or null when the tag is absent or no
    ///     digit starts it. The engine's integer tags are written as floats often enough to matter -
    ///     <c>Projectile_Blast_Area_Max_Victims</c> ships as <c>1.0</c> - and read as 1.
    /// </summary>
    public int? IntOf(string tagName)
    {
        return EngineNumber.ReadInt(ValueOf(tagName));
    }

    /// <summary>
    ///     True for an affirmative spelling of a boolean tag; false for a denial, an unknown spelling
    ///     and an absent tag. A flag whose default is ON reads through
    ///     <see cref="EngineBoolean.IsTrueUnlessDenied" /> on <see cref="ValueOf" /> instead.
    /// </summary>
    public bool BoolOf(string tagName)
    {
        return EngineBoolean.IsTrue(ValueOf(tagName));
    }

    /// <summary>
    ///     The outer XML of a tag's last occurrence, or null when the object does not carry it - for
    ///     the sub-object lists whose value is only whitespace and whose content is in the children.
    /// </summary>
    public string? FragmentOf(string tagName)
    {
        return Last(tagName)?.Fragment;
    }

    /// <summary>
    ///     The entry of this object's own layer that displaced a base value - overrode it, or merged
    ///     into it - or null when the tag is inherited, added, the object's own, or absent. A
    ///     repeatable additive tag contributes one entry per occurrence, the base's inherited ones
    ///     first, so this is not simply the first entry with the name.
    /// </summary>
    public EffectiveTag? DisplacingTag(string tagName)
    {
        if (Tags.IsDefaultOrEmpty) return null;

        return Tags.FirstOrDefault(t =>
            t.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase)
            && t.Provenance is VariantProvenance.Overridden or VariantProvenance.Merged);
    }

    private EffectiveTag? Last(string tagName)
    {
        if (Tags.IsDefaultOrEmpty) return null;

        return Tags.LastOrDefault(t => t.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase));
    }
}