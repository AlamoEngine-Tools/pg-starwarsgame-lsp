// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Opens <c>e2e-workspace/</c> - a small AUTHORED mod over the shipped-game baseline, rather than
///     a copy of the game. Tests that need a particular condition in the data write it there; the
///     shipped corpora have to stay faithful, so they cannot.
/// </summary>
public sealed class E2eModServerFixture : LspServerFixture
{
    protected override string ResolveWorkspacePath()
    {
        return LspTestEnvironment.E2eWorkspacePath ?? TestDataDirectory;
    }
}
