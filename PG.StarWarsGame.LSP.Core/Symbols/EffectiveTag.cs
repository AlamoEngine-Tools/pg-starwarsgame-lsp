// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     One tag of a fully merged (effective) object, with the provenance that tells the UX whether it was
///     inherited, overridden, merged, or added.
/// </summary>
/// <param name="TagName">The tag's element name.</param>
/// <param name="Value">The effective value after merging.</param>
/// <param name="Fragment">The effective outer XML (synthesized for merged tags).</param>
/// <param name="Provenance">How this value relates to the base chain.</param>
/// <param name="OriginObjectId">Id of the object in the chain that supplied the winning value.</param>
/// <param name="Origin">Where the winning value is defined, for go-to navigation. Null when unknown.</param>
/// <param name="BaseValue">
///     The value this tag held immediately before the winning layer applied - i.e. what the object
///     would have inherited had it not redefined the tag. Set for <see cref="VariantProvenance.Overridden" />
///     (the replaced value) and <see cref="VariantProvenance.Merged" /> (the value merged into), so the UX
///     can report "0 (was 99)" or explain what an additive tag was unioned with. Null for
///     <see cref="VariantProvenance.Own" />, <see cref="VariantProvenance.Added" /> and
///     <see cref="VariantProvenance.Inherited" />, where nothing was replaced.
/// </param>
public sealed record EffectiveTag(
    string TagName,
    string Value,
    string Fragment,
    VariantProvenance Provenance,
    string OriginObjectId,
    SymbolOrigin? Origin,
    string? BaseValue = null
);