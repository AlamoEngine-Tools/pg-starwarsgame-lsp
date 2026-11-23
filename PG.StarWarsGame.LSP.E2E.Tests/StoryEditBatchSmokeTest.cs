// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using PG.StarWarsGame.LSP.Server.Editing;
using PG.StarWarsGame.LSP.Server.Story;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     E2E smoke test for the visual editors' one pair (#339) against the vanilla foc/ workspace:
///     a story batch is checked and composed through the real LSP wire, so the envelope, the
///     commands in the shape the story graph sends them and the edit in the answer are exercised
///     together. The server composes and writes nothing, which the test holds it to.
/// </summary>
[Trait("Category", "E2E")]
public sealed class StoryEditBatchSmokeTest : IClassFixture<LspServerFixture>
{
    // The FoC registry ships one story campaign; EaW comments its main campaigns out.
    private const string Campaign = "Full_Story_Campaign_Underworld";
    private const string Faction = "Underworld";

    private static readonly EditBatchSubject Subject = new(Campaign, Faction);

    private readonly LspServerFixture _fixture;

    public StoryEditBatchSmokeTest(LspServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ValidateEditBatch_OfNoCommands_ChecksTheCampaignAsItStandsAsync()
    {
        RequireWorkspace();
        await WaitForScanAsync();

        using var cts = Timeout();
        var result = await _fixture.Client.SendRequest(
            new ValidateEditBatchParams(StoryEditBatchComposer.EditorName, Subject, []), cts.Token);

        Assert.Null(result.Error);
        // Every problem of a story batch is placed in a thread file.
        Assert.All(result.Problems, p => Assert.False(string.IsNullOrEmpty(p.Uri)));
    }

    [Fact]
    public async Task ApplyEditBatch_ComposesTheEditForTheEventsThread_AndWritesNothingAsync()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var target = await AnEventAsync();
        var threadPath = new Uri(target.ThreadUri!).LocalPath;
        var before = await File.ReadAllTextAsync(threadPath, TestContext.Current.CancellationToken);

        // As the story graph sends a staged change: camel case, only the fields the command uses.
        var commands = new JArray(new JObject
        {
            ["kind"] = "setBranch",
            ["threadUri"] = target.ThreadUri,
            ["eventName"] = target.Label,
            ["value"] = "E2E_Edit_Batch_Branch"
        });

        using var cts = Timeout();
        var result = await _fixture.Client.SendRequest(
            new ApplyEditBatchParams(StoryEditBatchComposer.EditorName, Subject, commands), cts.Token);

        Assert.True(result.Success, result.Error);
        var change = Assert.Single(result.Edit!.DocumentChanges!);
        var edit = change.TextDocumentEdit!;
        Assert.Equal(
            Path.GetFileName(threadPath),
            Path.GetFileName(edit.TextDocument.Uri.GetFileSystemPath()),
            ignoreCase: true);
        Assert.Contains("<Branch>E2E_Edit_Batch_Branch</Branch>", edit.Edits.Single().NewText);

        // Composed, not written: the edit is the client's to apply.
        Assert.Equal(before, await File.ReadAllTextAsync(threadPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ApplyEditBatch_FromAnEditorNothingWritesFor_IsRefusedAsync()
    {
        RequireWorkspace();
        await WaitForScanAsync();

        using var cts = Timeout();
        var result = await _fixture.Client.SendRequest(
            new ApplyEditBatchParams("no-such-editor", Subject, []), cts.Token);

        Assert.False(result.Success);
        Assert.Null(result.Edit);
        Assert.Contains("no-such-editor", result.Error);
    }

    /// <summary>An event of the campaign that sits in a thread file and has no branch yet.</summary>
    private async Task<StoryGraphNodeDto> AnEventAsync()
    {
        using var cts = Timeout();
        var graph = await _fixture.Client.SendRequest(new GetStoryGraphParams(Campaign, Faction), cts.Token);
        Assert.True(graph.Error is null, graph.Error);
        return graph.Nodes.First(n =>
            n.Kind == "Event" && !string.IsNullOrEmpty(n.ThreadUri) && string.IsNullOrEmpty(n.Branch));
    }

    private static CancellationTokenSource Timeout()
    {
        return new CancellationTokenSource(TimeSpan.FromSeconds(60));
    }

    private static void RequireWorkspace()
    {
        if (LspTestEnvironment.WorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception(
                "$XunitDynamicSkip$Set LSP_WORKSPACE_PATH and LSP_SCHEMA_LOCAL_PATH to run this test.");
    }

    private async Task WaitForScanAsync()
    {
        var completed = await Task.WhenAny(_fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(60)));
        if (completed != _fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 60 s.");
    }
}