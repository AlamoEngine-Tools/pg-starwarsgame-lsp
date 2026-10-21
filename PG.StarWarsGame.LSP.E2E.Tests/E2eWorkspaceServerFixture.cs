// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     A server on the suite's authored mod, alone or with a project beneath it (#280). The tests
///     written against <c>e2e-workspace/</c> run against both, and open the mod's files through
///     <see cref="ModPath" /> rather than the repository's copy.
/// </summary>
public abstract class E2eWorkspaceServerFixture : LspServerFixture
{
    /// <summary>The folder holding the mod's project and files; null without <c>e2e-workspace/</c>.</summary>
    public abstract string? ModPath { get; }

    /// <summary>The project layers the server resolves: the mod, and the base beneath it if any.</summary>
    public abstract int Layers { get; }
}