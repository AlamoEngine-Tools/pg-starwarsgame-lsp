// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Caching;

public sealed class NullProjectIndexCache : IProjectIndexCache
{
    public ProjectIndexSnapshot? TryLoad(string pgprojPath)
    {
        return null;
    }

    public void Save(string pgprojPath, ProjectIndexSnapshot snapshot)
    {
    }

    public void EnsureGitHygiene(string pgprojPath)
    {
    }
}