// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Preview;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     <c>aet/listModels</c> against the real server, workspace and baseline.
/// </summary>
/// <remarks>
///     The picker exists for models the author cannot see in the explorer - in a dependency or inside
///     a shipped MEG. The authored workspace ships no model at all, so every entry here has to come
///     through the baseline's catalog, which only a real server with a real baseline exercises.
/// </remarks>
[Trait("Category", "E2E")]
public sealed class ListModelsSmokeTest(E2eModServerFixture fixture) : IClassFixture<E2eModServerFixture>
{
    [Fact]
    public async Task BaseGameModels_AreOfferedAndMarkedAsSuchAsync()
    {
        if (LspTestEnvironment.E2eWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$e2e-workspace/ not found; cannot run the model list test.");

        var completed = await Task.WhenAny(fixture.ScanCompleted,
            Task.Delay(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
        if (completed != fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 60 s.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await fixture.Client.SendRequest(new ListModelsParams(), cts.Token);

        var destroyer = Assert.Single(result.Models,
            m => m.Name.Equals("ev_stardestroyer.alo", StringComparison.OrdinalIgnoreCase));
        Assert.True(destroyer.BaseGame);
        // Hundreds, not a handful: a list that stopped at the workspace's own folder would be empty
        // here, and one that read a single MEG would be far short of the shipped catalog.
        Assert.True(result.Models.Count > 500, $"Only {result.Models.Count} models listed");
    }
}
