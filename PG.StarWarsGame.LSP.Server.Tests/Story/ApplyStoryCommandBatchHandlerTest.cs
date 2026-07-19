// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Story;
using static PG.StarWarsGame.LSP.Server.Tests.Story.StoryCommandTestFixtures;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

public sealed class ApplyStoryCommandBatchHandlerTest
{
    private static ApplyStoryCommandBatchHandler Handler(
        CapturingApplier applier, bool storyEditor = true, GameIndex? index = null,
        bool storyEditing = true)
    {
        var fs = NewFileSystem();
        var fileHelper = FileHelperFor(fs);
        return new ApplyStoryCommandBatchHandler(
            new StubModelService(),
            new StubIndexService(index ?? DefaultIndex()),
            TextSource(fileHelper),
            new StoryTestSchema(),
            fileHelper,
            Reload(),
            applier,
            Config(storyEditor, storyEditing),
            NullLogger<ApplyStoryCommandBatchHandler>.Instance);
    }

    private static ApplyStoryCommandBatchParams Batch(params StoryCommandDto[] commands)
    {
        return new ApplyStoryCommandBatchParams("GC", commands);
    }

    /// <summary>Final text per URI - each changed file is a whole-document replacement (one edit).</summary>
    private static Dictionary<string, string> FinalTexts(WorkspaceEdit edit)
    {
        return edit.DocumentChanges!
            .Where(c => c.IsTextDocumentEdit)
            .ToDictionary(
                c => c.TextDocumentEdit!.TextDocument.Uri.ToString(),
                c => c.TextDocumentEdit!.Edits.Single().NewText);
    }

    [Fact]
    public async Task StoryEditorOff_ReturnsDisabledMessage()
    {
        var result = await Handler(new CapturingApplier(), false)
            .Handle(Batch(Cmd("setPerpetual", eventName: "Start", flag: true)), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(StoryEditorFeature.DisabledMessage, result.Error);
    }

    /// <summary>
    ///     The one that actually writes to disk - with Edit mode off it must refuse and, critically,
    ///     hand nothing to the applier.
    /// </summary>
    [Fact]
    public async Task StoryEditingOff_ReturnsDisabledMessage_AndAppliesNothing()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier, storyEditing: false)
            .Handle(Batch(Cmd("setPerpetual", eventName: "Start", flag: true)), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(StoryEditingFeature.DisabledMessage, result.Error);
        Assert.Null(applier.Edit);
    }

    [Fact]
    public async Task EmptyBatch_SucceedsWithoutAnyEdit()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier).Handle(Batch(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(applier.Edit);
    }

    [Fact]
    public async Task CreateThenModify_SecondCommandSeesTheFirst_InOneEdit()
    {
        // setPerpetual can only locate "Fresh" if createEvent's insert was composed into the working
        // text first - this is the composition guarantee the batch endpoint exists to provide.
        var applier = new CapturingApplier();

        var result = await Handler(applier).Handle(Batch(
            Cmd("createEvent", newName: "Fresh", eventType: "STORY_TRIGGER"),
            Cmd("setPerpetual", eventName: "Fresh", flag: true)), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.FailedIndex);
        var finalText = Assert.Single(FinalTexts(applier.Edit!)).Value;
        Assert.Contains("<Event Name=\"Fresh\">", finalText);
        Assert.Contains("<Perpetual>Yes</Perpetual>", finalText);
    }

    [Fact]
    public async Task FirstCommandFails_AbortsWholeBatch_WritesNothing_ReturnsFailedIndex()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier).Handle(Batch(
                Cmd("setPerpetual", eventName: "Start", flag: true), // valid
                Cmd("deleteEvent", eventName: "Ghost")), // invalid → aborts the batch
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(1, result.FailedIndex);
        Assert.Contains("Ghost", result.Error);
        Assert.Null(applier.Edit); // nothing written
    }

    [Fact]
    public async Task CrossFileBatch_TouchesManifestAndThread_InOneEdit()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier).Handle(Batch(
            Cmd("attachLuaScript", file: "story_plots_r.xml", value: "Story_Script"),
            Cmd("setEventType", eventName: "Start", value: "STORY_FLAGS")), CancellationToken.None);

        Assert.True(result.Success);
        var finals = FinalTexts(applier.Edit!);
        Assert.Equal(2, finals.Count);
        Assert.Contains("<Lua_Script>Story_Script</Lua_Script>", finals[ManifestUri]);
        Assert.Contains("<Event_Type>STORY_FLAGS</Event_Type>", finals[ThreadUri]);
    }

    [Fact]
    public async Task RenameOfIndexedStorySymbol_ComposesViaModelPath_DoesNotBlockTheBatch()
    {
        // "Start" is indexed as a StoryEvent symbol, so the single-command handler would take the
        // opaque symbol-index rename path. In a batch that path can't compose - this must instead
        // fall back to the model rename (re-parses the working text) and succeed.
        var startSymbol = new GameSymbol("Start", GameSymbolKind.XmlObject, "StoryEvent",
            new FileOrigin(ThreadUri, 1, 13), null);
        var index = DefaultIndex() with
        {
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .WithComparers(StringComparer.OrdinalIgnoreCase)
                .Add("Start", [startSymbol])
        };
        var applier = new CapturingApplier();

        var result = await Handler(applier, index: index).Handle(Batch(
            Cmd("setPerpetual", eventName: "Start", flag: true),
            Cmd("renameEvent", eventName: "Start", newName: "Renamed")), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.FailedIndex);
        var finalText = Assert.Single(FinalTexts(applier.Edit!)).Value;
        Assert.Contains("<Event Name=\"Renamed\">", finalText);
        Assert.Contains("<Prereq>Renamed</Prereq>", finalText); // the reference from "Next" moved too
        Assert.Contains("<Perpetual>Yes</Perpetual>", finalText); // the earlier staged edit survived
    }

    [Fact]
    public async Task CreateThread_EmitsCreateFileAndManifestEntry()
    {
        var applier = new CapturingApplier();

        var result = await Handler(applier).Handle(
            Batch(Cmd("createThread", file: "story_plots_r.xml", value: "story_new.xml")),
            CancellationToken.None);

        Assert.True(result.Success);
        var changes = applier.Edit!.DocumentChanges!.ToList();
        Assert.Contains(changes, c => c.IsCreateFile && c.CreateFile!.Uri.ToString().EndsWith("story_new.xml"));
        Assert.Contains("<Active_Plot>story_new.xml</Active_Plot>", FinalTexts(applier.Edit!)[ManifestUri]);
    }
}