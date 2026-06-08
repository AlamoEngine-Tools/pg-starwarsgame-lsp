// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Assets;

/// <summary>
///     Merged (baseline ∪ workspace) catalog of game-asset file paths, used for existence
///     validation and completion of <c>textureFile</c>, <c>modelFile</c>, <c>audioFile</c> and
///     <c>mapFile</c> references. Paths are normalised (lowercase, forward-slash) and matched
///     case-insensitively.
/// </summary>
public interface IAssetFileIndex
{
    /// <summary>True if the catalog contains <paramref name="normalisedPath" /> (case-insensitive).</summary>
    bool Contains(string normalisedPath);

    /// <summary>All catalog paths whose extension equals <paramref name="ext" /> (e.g. <c>.tga</c>).</summary>
    IEnumerable<string> GetByExtension(string ext);

    /// <summary>
    ///     True when the asset at <paramref name="normalisedPath" /> originates from the baseline
    ///     (i.e. is packed inside a MEG archive in the shipped game). Returns <see langword="false" />
    ///     for loose workspace files or when the path is not in the catalog.
    /// </summary>
    bool IsPackedAsset(string normalisedPath);
}