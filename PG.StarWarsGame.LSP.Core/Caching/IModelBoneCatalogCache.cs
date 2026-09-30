// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Caching;

/// <summary>
///     Persists one layer's extracted model-bone catalog beside its <c>.pgproj</c>, so a restart
///     re-parses <c>.alo</c> models only where the tree actually changed.
/// </summary>
public interface IModelBoneCatalogCache
{
    /// <summary>
    ///     The bones saved for <paramref name="pgprojPath" />, or <see langword="null" /> when
    ///     nothing is saved, the snapshot is unreadable, it was written by a different extractor
    ///     version, or <paramref name="fingerprint" /> does not match the one it was built from.
    /// </summary>
    IReadOnlyDictionary<string, ModelCatalogEntry>? TryLoad(string pgprojPath, string fingerprint);

    void Save(string pgprojPath, string fingerprint, IReadOnlyDictionary<string, ModelCatalogEntry> models);
}