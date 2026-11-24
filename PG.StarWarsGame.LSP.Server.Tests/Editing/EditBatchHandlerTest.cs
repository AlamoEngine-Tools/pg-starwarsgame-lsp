// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Editing;
using PG.StarWarsGame.LSP.Server.Story;
using static PG.StarWarsGame.LSP.Server.Tests.Story.StoryCommandTestFixtures;

namespace PG.StarWarsGame.LSP.Server.Tests.Editing;

/// <summary>
///     The editors' one pair (#339): a batch goes to the composer of the editor that sent it, and a
///     batch from an editor nothing here writes for is refused with a message that names it.
/// </summary>
public sealed class EditBatchHandlerTest
{
    private static readonly EditBatchSubject AnySubject = new("GC", "Rebel");

    // No project loaded: the layer rule has nothing to hold an edit against (EditBatchLayerGuardTest has the rule).
    private static readonly EditBatchLayerGuard NoLayers = new(
        new ProjectLayerMap(new FileHelper(new MockFileSystem())),
        new FileHelper(new MockFileSystem()),
        TestCatalogues.English);

    private static EditBatchComposers Composers(params IEditBatchComposer[] composers)
    {
        return new EditBatchComposers(composers);
    }

    [Fact]
    public async Task Validate_GoesToTheComposerOfTheBatchsEditor()
    {
        var story = new RecordingComposer("story");
        var credits = new RecordingComposer("credits");
        var commands = new JArray(new JObject { ["kind"] = "setCell" });

        var result = await new ValidateEditBatchHandler(Composers(story, credits), TestCatalogues.English)
            .Handle(new ValidateEditBatchParams("credits", AnySubject, commands), CancellationToken.None);

        Assert.Equal("credits", Assert.Single(result.Problems).Message);
        Assert.Empty(story.Validated);
        Assert.Same(commands, Assert.Single(credits.Validated).Commands);
        Assert.Same(AnySubject, Assert.Single(credits.Validated).Subject);
    }

    [Fact]
    public async Task Apply_GoesToTheComposerOfTheBatchsEditor()
    {
        var story = new RecordingComposer("story");
        var credits = new RecordingComposer("credits");
        var commands = new JArray(new JObject { ["kind"] = "setDialog" });

        var result = await new ApplyEditBatchHandler(Composers(story, credits), NoLayers, TestCatalogues.English)
            .Handle(new ApplyEditBatchParams("story", AnySubject, commands), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Same(story.Edit, result.Edit);
        Assert.Empty(credits.Composed);
        Assert.Same(commands, Assert.Single(story.Composed).Commands);
    }

    [Fact]
    public async Task Validate_FromAnEditorNothingWritesFor_SaysWhichEditor()
    {
        var story = new RecordingComposer("story");

        var result = await new ValidateEditBatchHandler(Composers(story), TestCatalogues.English)
            .Handle(new ValidateEditBatchParams("models", AnySubject, []), CancellationToken.None);

        Assert.Empty(result.Problems);
        Assert.Equal("No editor 'models' writes through this server.", result.Error);
        Assert.Empty(story.Validated);
    }

    [Fact]
    public async Task Apply_FromAnEditorNothingWritesFor_IsRefusedAndComposesNothing()
    {
        var story = new RecordingComposer("story");

        var result = await new ApplyEditBatchHandler(Composers(story), NoLayers, TestCatalogues.English)
            .Handle(new ApplyEditBatchParams("models", AnySubject, []), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Edit);
        Assert.Equal("No editor 'models' writes through this server.", result.Error);
        Assert.Empty(story.Composed);
    }

    /// <summary>
    ///     The commands arrive as the story graph sends them: camel case, only the fields a command
    ///     uses. Read through the whole pair, so the envelope and the composer agree on the shape.
    /// </summary>
    [Fact]
    public async Task Apply_ReadsTheStoryGraphsCommandsAsItSendsThem()
    {
        var commands = new JArray(new JObject
        {
            ["kind"] = "setDialog", ["threadUri"] = ThreadUri, ["eventName"] = "Next", ["value"] = "DIALOG_X"
        });

        var result = await new ApplyEditBatchHandler(Composers(Composer()), NoLayers, TestCatalogues.English)
            .Handle(
                new ApplyEditBatchParams(StoryEditBatchComposer.EditorName, Subject, commands),
                CancellationToken.None);

        Assert.True(result.Success, result.Error);
        var change = Assert.Single(result.Edit!.DocumentChanges!);
        Assert.Contains("<Story_Dialog>DIALOG_X</Story_Dialog>", change.TextDocumentEdit!.Edits.Single().NewText);
    }

    [Fact]
    public async Task Apply_WithCommandsThatAreNotCommands_IsRefusedWithTheReason()
    {
        var result = await new ApplyEditBatchHandler(Composers(Composer()), NoLayers, TestCatalogues.English)
            .Handle(
                new ApplyEditBatchParams(StoryEditBatchComposer.EditorName, Subject, JArray.Parse("[[1, 2]]")),
                CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Edit);
        Assert.StartsWith("The staged changes could not be read: ", result.Error);
    }

    private sealed class RecordingComposer(string editor) : IEditBatchComposer
    {
        public List<(EditBatchSubject Subject, JArray Commands)> Validated { get; } = [];
        public List<(EditBatchSubject Subject, JArray Commands)> Composed { get; } = [];
        public WorkspaceEdit Edit { get; } = new();

        public string Editor => editor;

        public Task<ValidateEditBatchResult> ValidateAsync(
            EditBatchSubject subject, JArray commands, CancellationToken ct)
        {
            Validated.Add((subject, commands));
            return Task.FromResult(new ValidateEditBatchResult([new EditBatchProblemDto("info", editor)]));
        }

        public Task<ApplyEditBatchResult> ComposeAsync(
            EditBatchSubject subject, JArray commands, CancellationToken ct)
        {
            Composed.Add((subject, commands));
            return Task.FromResult(new ApplyEditBatchResult(true, Edit: Edit));
        }
    }
}