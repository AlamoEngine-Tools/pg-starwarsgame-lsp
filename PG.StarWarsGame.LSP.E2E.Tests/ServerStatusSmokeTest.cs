// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Status;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     <c>aet/getServerStatus</c> against a real startup: every value it reports is written by a
///     different startup step, and only a real server runs all of them in order.
/// </summary>
[Trait("Category", "E2E")]
public sealed class ServerStatusSmokeTest(E2eModServerFixture fixture)
    : ServerStatusSmokeTestBase<E2eModServerFixture>(fixture);

/// <summary>The mod over a base project (#280).</summary>
[Trait("Category", "E2E")]
public sealed class ServerStatusMultiRootSmokeTest(E2eMultiRootServerFixture fixture)
    : ServerStatusSmokeTestBase<E2eMultiRootServerFixture>(fixture);

public abstract class ServerStatusSmokeTestBase<TFixture>(TFixture fixture) : IClassFixture<TFixture>
    where TFixture : E2eWorkspaceServerFixture
{
    [Fact]
    public async Task AHealthyStartup_ReportsItselfAsHealthyAsync()
    {
        if (LspTestEnvironment.E2eWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$e2e-workspace/ not found; cannot run the status test.");

        var completed = await Task.WhenAny(fixture.ScanCompleted,
            Task.Delay(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
        if (completed != fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 60 s.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var status = await fixture.Client.SendRequest(new GetServerStatusParams(), cts.Token);

        // The fixture serves the schema and the baseline from disk.
        Assert.Equal("Local", status.Schema.Source);
        Assert.Equal("Local", status.Baseline.Source);
        Assert.NotNull(status.Baseline.BuiltAt);
        Assert.Equal("Complete", status.Index.State);
        Assert.True(status.Workspace.ProjectDetected);
        Assert.True(status.Workspace.ProjectValid);
        Assert.Empty(status.Warnings);
        Assert.Null(status.Extended);
    }

    /// <summary>
    ///     The extended counts come from the indexer, the resolver and the catalog after a real scan;
    ///     a stub could only prove the arithmetic.
    /// </summary>
    [Fact]
    public async Task TheExtendedTier_CountsWhatTheScanProducedAsync()
    {
        if (LspTestEnvironment.E2eWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$e2e-workspace/ not found; cannot run the status test.");

        var completed = await Task.WhenAny(fixture.ScanCompleted,
            Task.Delay(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
        if (completed != fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 60 s.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var extended = (await fixture.Client.SendRequest(
            new GetServerStatusParams { Extended = true }, cts.Token)).Extended;

        Assert.NotNull(extended);
        Assert.True(extended.Files.ProjectFiles > 0, "The authored workspace's own XML was not counted");
        Assert.True(extended.Symbols.Baseline > 1000, $"Only {extended.Symbols.Baseline} baseline symbols");
        Assert.Contains(extended.Assets, a => a is { Extension: ".alo", BaseGame: > 500 });
        // The mod, and in the two-project window the base beneath it, whose own XML counts apart.
        Assert.Equal(fixture.Layers, extended.Dependencies.Layers);
        if (fixture.Layers > 1)
            Assert.True(extended.Files.DependencyFiles > 0, "The base project's XML was not counted");
        Assert.NotNull(extended.IndexDurationMs);
        Assert.Equal(extended.Files.ProjectFiles + extended.Files.DependencyFiles,
            extended.Caches.FilesReused + extended.Caches.FilesParsed);
        Assert.NotEmpty(extended.SymbolTypes);
    }
}