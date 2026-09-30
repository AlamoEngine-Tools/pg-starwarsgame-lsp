// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Caching;

/// <summary>
///     What one <c>.alo</c> contributes to the workspace catalogs: the bone names a
///     <c>boneName</c> reference can resolve against, and the texture names the model carries
///     inside itself.
/// </summary>
/// <remarks>
///     One entry rather than two parallel maps so the two can never disagree about which models
///     were scanned - and "was this model scanned" is the question both callers actually ask. An
///     EMPTY list on either side is a real answer; a model missing from the map is what means
///     "never scanned", and the caller must then open the file rather than treat it as empty.
/// </remarks>
/// <param name="Bones">Skeleton bones unioned with mesh names.</param>
/// <param name="Textures">Texture names bound inside the model or particle system.</param>
public sealed record ModelCatalogEntry(string[] Bones, string[] Textures)
{
    public static readonly ModelCatalogEntry Empty = new([], []);
}
