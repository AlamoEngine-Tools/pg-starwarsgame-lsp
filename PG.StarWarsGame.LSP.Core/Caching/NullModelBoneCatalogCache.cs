// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Caching;

/// <summary>
///     Never reuses and never persists - every call extracts afresh. The behaviour the indexer had
///     before snapshots existed, and the right default for a test that is not about caching.
/// </summary>
public sealed class NullModelBoneCatalogCache : IModelBoneCatalogCache
{
    public IReadOnlyDictionary<string, ModelCatalogEntry>? TryLoad(string pgprojPath, string fingerprint)
    {
        return null;
    }

    public void Save(
        string pgprojPath, string fingerprint, IReadOnlyDictionary<string, ModelCatalogEntry> models)
    {
    }
}