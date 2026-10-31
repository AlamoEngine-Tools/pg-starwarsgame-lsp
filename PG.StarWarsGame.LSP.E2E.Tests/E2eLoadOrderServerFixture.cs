// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Opens <c>e2e-workspace-load-order/</c>: an AUTHORED FoC mod whose own object and targeting-set
///     registries list one shipped file it does not ship, one it replaces with its own copy, and files
///     of its own that define names again. See the comments in its files for what each case expects.
/// </summary>
// E2e is spelled as a word throughout the E2E test project.
// ReSharper disable once InconsistentNaming
public sealed class E2eLoadOrderServerFixture : E2eWorkspaceServerFixture
{
    public override string? ModPath => LspTestEnvironment.E2eLoadOrderWorkspacePath;

    public override int Layers => 1;

    protected override string ResolveWorkspacePath()
    {
        return LspTestEnvironment.E2eLoadOrderWorkspacePath ?? TestDataDirectory;
    }
}