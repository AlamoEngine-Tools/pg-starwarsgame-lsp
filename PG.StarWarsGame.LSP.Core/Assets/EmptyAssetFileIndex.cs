// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Assets;

/// <summary>Empty fallback used by <see cref="Symbols.GameIndex.Empty" /> before any catalog is applied.</summary>
public sealed class EmptyAssetFileIndex : IAssetFileIndex
{
    public static readonly EmptyAssetFileIndex Instance = new();

    private EmptyAssetFileIndex()
    {
    }

    public bool Contains(string normalisedPath)
    {
        return false;
    }

    public IEnumerable<string> GetByExtension(string ext)
    {
        return [];
    }

    public bool IsPackedAsset(string normalisedPath)
    {
        return false;
    }
}