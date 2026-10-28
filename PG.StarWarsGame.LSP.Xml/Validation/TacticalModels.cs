// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>Whether an object can be assembled into a scene: whether it names a tactical model.</summary>
public static class TacticalModels
{
    /// <summary>
    ///     Whether this object, or anything it varies, names a tactical model.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A tag scan up the variant chain rather than a full
    ///         <c>EffectiveObjectResolver.Resolve</c>: a code lens request runs over every symbol in
    ///         the document and fires again on every edit, so the per-symbol cost has to stay at the
    ///         level the encyclopedia lens already pays. The question here is only "is there a model
    ///         at all", which needs no schema merge to answer.
    ///     </para>
    ///     <para>
    ///         The chain matters. A variant typically restates only what it changes, so most objects
    ///         in a mod inherit their model; gating on restating it would leave the lens off exactly
    ///         the objects whose assembled result is worth looking at.
    ///     </para>
    /// </remarks>
    public static bool Declared(GameIndex index, IVariantTagSource tagSource, GameSymbol symbol)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = symbol;

        // `visited` also terminates a cycle. A mod can write one, and the preview reports it as a
        // problem rather than refusing - but a lens that hung the editor would be unforgivable.
        while (current is not null && visited.Add(current.Id))
        {
            if (LayerDeclaresModel(index, tagSource, current))
                return true;

            current = index.VariantBaseOf(current);
        }

        return false;
    }

    private static bool LayerDeclaresModel(GameIndex index, IVariantTagSource tagSource, GameSymbol symbol)
    {
        // The workspace SHADOWS the baseline rather than adding to it - a mod that redefines an
        // object replaces its tags, so a baseline model must not answer for a workspace object that
        // dropped it.
        var workspace = tagSource.TryGetTags(symbol);
        if (workspace is not null)
            return workspace.Any(tag => IsModelTag(tag.TagName));

        return index.Baseline.ObjectTags.TryGetValue(symbol.Id, out var baseline)
               && baseline.Any(tag => IsModelTag(tag.TagName));
    }

    private static bool IsModelTag(string tagName)
    {
        return HardpointBoneModelResolver.AttachingObjectModelTags
            .Contains(tagName, StringComparer.OrdinalIgnoreCase);
    }
}