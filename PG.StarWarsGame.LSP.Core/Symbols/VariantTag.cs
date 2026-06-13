// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     A single direct child tag of an object, as fed to the <see cref="EffectiveObjectResolver" /> by an
///     <see cref="IVariantTagSource" />. Unifies workspace tags (from a live XML node) and baseline tags
///     (from <see cref="BaselineTag" />) so the merge engine is source-agnostic.
/// </summary>
/// <param name="TagName">The tag's element name.</param>
/// <param name="Value">The tag's trimmed inner-text value.</param>
/// <param name="Fragment">The tag's verbatim outer XML.</param>
/// <param name="StartLine">0-based line of the tag in its source document.</param>
/// <param name="Origin">Where the tag is defined, for go-to navigation. Null when unknown.</param>
public sealed record VariantTag(
    string TagName,
    string Value,
    string Fragment,
    int StartLine,
    SymbolOrigin? Origin = null
);
