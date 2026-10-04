// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Caching;

/// <summary>
///     Persisted index snapshots, one per (layer, context). The context key is the layer's
///     <see cref="CrossLayerInputFingerprint" />: the same dependency can parse differently under
///     different leaves, and each such context keeps its own snapshot.
/// </summary>
public interface IProjectIndexCache
{
    ProjectIndexSnapshot? TryLoad(string pgprojPath, string contextKey);
    void Save(string pgprojPath, string contextKey, ProjectIndexSnapshot snapshot);
    void EnsureGitHygiene(string pgprojPath);
}