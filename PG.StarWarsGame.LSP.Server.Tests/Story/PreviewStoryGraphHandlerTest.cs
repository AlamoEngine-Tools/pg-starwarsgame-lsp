// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Story;
using static PG.StarWarsGame.LSP.Server.Tests.Story.StoryCommandTestFixtures;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

public sealed class PreviewStoryGraphHandlerTest
{
    private static PreviewStoryGraphHandler Handler(bool storyEditor = true, bool storyEditing = true)
    {
        var fs = NewFileSystem();
        var fileHelper = FileHelperFor(fs);
        return new PreviewStoryGraphHandler(
            new StubModelService(),
            new StubIndexService(DefaultIndex()),
            TextSource(fileHelper),
            new StoryTestSchema(),
            fileHelper,
            Reload(),
            Config(storyEditor, storyEditing));
    }

    private static PreviewStoryGraphParams Preview(params StoryCommandDto[] commands)
    {
        return new PreviewStoryGraphParams("GC", commands);
    }

    [Fact]
    public async Task StoryEditorOff_ReturnsDisabledMessage()
    {
        var result = await Handler(false).Handle(Preview(), CancellationToken.None);

        Assert.Equal(StoryEditorFeature.DisabledMessage, result.Error);
    }

    /// <summary>
    ///     Previewing is an authoring operation: with the panel enabled for read-only viewing but
    ///     Edit mode off, it must be refused - and name the editing flag, not the panel flag.
    /// </summary>
    [Fact]
    public async Task StoryEditingOff_ReturnsDisabledMessage()
    {
        var result = await Handler(storyEditing: false).Handle(Preview(), CancellationToken.None);

        Assert.Equal(StoryEditingFeature.DisabledMessage, result.Error);
    }

    [Fact]
    public async Task NoCommands_ReturnsCommittedGraph()
    {
        var result = await Handler().Handle(Preview(), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Contains(result.Nodes, n => n.Label == "Start");
        Assert.Contains(result.Nodes, n => n.Label == "Next");
        Assert.DoesNotContain(result.Nodes, n => n.Label == "Fresh");
    }

    [Fact]
    public async Task StagedCreateEvent_AppearsInPreview()
    {
        // The new event exists only in the composed working copy (nothing was written) - its node
        // in the preview proves the model was re-assembled from the staged text.
        var result = await Handler().Handle(
            Preview(Cmd("createEvent", newName: "Fresh", eventType: "STORY_TRIGGER")), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Contains(result.Nodes, n => n.Label == "Fresh");
    }

    [Fact]
    public async Task StagedDeleteEvent_RemovesNodeFromPreview()
    {
        var result = await Handler().Handle(
            Preview(Cmd("deleteEvent", eventName: "Next")), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Contains(result.Nodes, n => n.Label == "Start");
        Assert.DoesNotContain(result.Nodes, n => n.Label == "Next");
    }

    [Fact]
    public async Task StagedCommandThatFails_ReturnsError()
    {
        var result = await Handler().Handle(
            Preview(Cmd("deleteEvent", eventName: "Ghost")), CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Contains("Ghost", result.Error);
    }
}