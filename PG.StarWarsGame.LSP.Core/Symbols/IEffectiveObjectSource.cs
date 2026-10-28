// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     Resolves an object id to its effective (variant-merged) tag set.
/// </summary>
/// <remarks>
///     The seam that lets a diagnostic ask about an object OTHER than the one being edited - "does
///     the thing this tag names have behaviour X", "what is that object's health". Everything
///     needed for it already existed (<see cref="BaselineIndex.ObjectTags" /> for shipped data,
///     <c>WorkspaceVariantTagSource</c> for the mod, <see cref="EffectiveObjectResolver" /> to merge
///     them); what was missing was a way to reach it from a handler, which is all this interface is.
///     <para>
///         Narrow on purpose: a handler needs one question answered, and the concrete resolver
///         carries construction requirements a test should not have to satisfy.
///     </para>
/// </remarks>
public interface IEffectiveObjectSource
{
    /// <summary>
    ///     The merged object. Never null: an unknown id comes back with
    ///     <see cref="EffectiveObject.Found" /> false, so callers distinguish "has no such
    ///     behaviour" from "no such object" rather than reporting both as the same fault.
    /// </summary>
    EffectiveObject Resolve(string objectId);

    /// <summary>
    ///     The merged object named <paramref name="objectId" /> in the id space of
    ///     <paramref name="typeName" />. Every type is an id space of its own, so a caller that
    ///     knows what it is asking for - a unit, a hardpoint - asks here.
    /// </summary>
    EffectiveObject Resolve(string objectId, string typeName);
}

public static class EffectiveObjectSources
{
    /// <summary>
    ///     The object in the id space of <paramref name="typeName" />, or by id alone when the caller
    ///     has no type - a tag the schema gives no reference type.
    /// </summary>
    public static EffectiveObject ResolveIn(this IEffectiveObjectSource source, string objectId, string? typeName)
    {
        return typeName is null ? source.Resolve(objectId) : source.Resolve(objectId, typeName);
    }
}