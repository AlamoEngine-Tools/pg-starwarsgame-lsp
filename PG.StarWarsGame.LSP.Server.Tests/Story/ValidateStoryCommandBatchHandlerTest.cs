// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Story;
using PG.StarWarsGame.LSP.Story.Graph;
using static PG.StarWarsGame.LSP.Server.Tests.Story.StoryCommandTestFixtures;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

public sealed class ValidateStoryCommandBatchHandlerTest
{
    private static ValidateStoryCommandBatchHandler Handler(
        string marker, bool storyEditor = true, bool storyEditing = true)
    {
        var fs = NewFileSystem();
        var fileHelper = FileHelperFor(fs);
        return new ValidateStoryCommandBatchHandler(
            new StubModelService(),
            new StubIndexService(DefaultIndex()),
            TextSource(fileHelper),
            new StoryTestSchema(),
            fileHelper,
            Reload(),
            new MarkerCollector(marker),
            Config(storyEditor, storyEditing));
    }

    private static ValidateStoryCommandBatchParams Batch(params StoryCommandDto[] commands)
    {
        return new ValidateStoryCommandBatchParams("GC", commands);
    }

    [Fact]
    public async Task StoryEditorOff_ReturnsDisabledMessage()
    {
        var result = await Handler("anything", false)
            .Handle(Batch(), CancellationToken.None);

        Assert.Equal(StoryEditorFeature.DisabledMessage, result.Error);
    }

    /// <summary>
    ///     Validation must survive Edit mode being off. The panel's Validate button renders in every
    ///     mode and is the only source of story diagnostics there, so gating this on the editing flag
    ///     silently left read-only users with none - regression guard for exactly that.
    /// </summary>
    [Fact]
    public async Task StoryEditingOff_StillValidates()
    {
        var result = await Handler("STORY_ELAPSED", storyEditing: false)
            .Handle(Batch(), CancellationToken.None);

        Assert.Null(result.Error);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(StoryGraphBuilder.EventNodeId(ThreadUri, "Start"), diagnostic.NodeId);
    }

    [Fact]
    public async Task NoBatch_DiagnosesCommittedText()
    {
        // STORY_ELAPSED is the committed trigger of "Start" - with no staged commands the validation
        // runs over buffer text and pins the marker to Start's node.
        var result = await Handler("STORY_ELAPSED").Handle(Batch(), CancellationToken.None);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(StoryGraphBuilder.EventNodeId(ThreadUri, "Start"), diagnostic.NodeId);
    }

    [Fact]
    public async Task StagedEdit_DiagnosesPendingText_WithoutTouchingTheBuffer()
    {
        // The marker exists nowhere in the committed text - only the staged setDialog introduces it.
        // A diagnostic for it proves the collector ran over the composed (pending) text.
        var handler = Handler("ZZBAD");

        var empty = await handler.Handle(Batch(), CancellationToken.None);
        Assert.Empty(empty.Diagnostics); // committed text has no marker

        var staged = await handler.Handle(
            Batch(Cmd("setDialog", eventName: "Start", value: "ZZBAD")), CancellationToken.None);

        var diagnostic = Assert.Single(staged.Diagnostics);
        Assert.Equal(StoryGraphBuilder.EventNodeId(ThreadUri, "Start"), diagnostic.NodeId);
        Assert.Contains("ZZBAD", diagnostic.Message);
    }

    [Fact]
    public async Task StagedCommandThatFails_ShortCircuitsWithThatError()
    {
        var result = await Handler("anything")
            .Handle(Batch(Cmd("deleteEvent", eventName: "Ghost")), CancellationToken.None);

        Assert.Empty(result.Diagnostics);
        Assert.Contains("Ghost", result.Error);
    }
}