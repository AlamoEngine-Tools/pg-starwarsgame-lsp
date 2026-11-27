// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Story;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     E2E smoke test for <c>aet/getStorySchema</c> against the real schema: the story graph's
///     palette is built from this answer alone, so what a type is called on its tile has to arrive
///     over the wire and not only exist in a file.
/// </summary>
[Trait("Category", "E2E")]
public sealed class StorySchemaSmokeTest : IClassFixture<LspServerFixture>
{
    private readonly LspServerFixture _fixture;

    public StorySchemaSmokeTest(LspServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetStorySchema_NamesEveryEventAndRewardType_ForItsTileAsync()
    {
        RequireWorkspace();

        // The schema loads in the background at start, and the request answers with no types until
        // it has: asked first thing, it returned two empty lists.
        var loaded = await Task.WhenAny(_fixture.ScanCompleted,
            Task.Delay(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
        if (loaded != _fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 60 s.");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var result = await _fixture.Client.SendRequest(new GetStorySchemaParams(), cts.Token);

        Assert.Null(result.Error);
        Assert.NotEmpty(result.EventTypes);
        Assert.NotEmpty(result.RewardTypes);

        var unnamed = result.EventTypes.Concat(result.RewardTypes)
            .Where(t => string.IsNullOrWhiteSpace(t.Label))
            .Select(t => t.Name)
            .ToList();
        Assert.True(unnamed.Count == 0, "Types that arrive without a display name: " + string.Join(", ", unnamed));

        // One of each by its words, so a label that arrives but belongs to another type is seen.
        Assert.Equal("Time elapsed", Assert.Single(result.EventTypes, t => t.Name == "STORY_ELAPSED").Label);
        Assert.Equal("Trigger event", Assert.Single(result.RewardTypes, t => t.Name == "TRIGGER_EVENT").Label);
    }

    private static void RequireWorkspace()
    {
        if (LspTestEnvironment.WorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception(
                "$XunitDynamicSkip$Set LSP_WORKSPACE_PATH and LSP_SCHEMA_LOCAL_PATH to run this test.");
    }
}