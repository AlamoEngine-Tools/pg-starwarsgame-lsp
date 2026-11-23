// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Server.Story;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Story.Graph;
using static PG.StarWarsGame.LSP.Server.Tests.Story.StoryCommandTestFixtures;

namespace PG.StarWarsGame.LSP.Server.Tests.Story;

public sealed class StoryEditBatchValidateTest
{
    private static StoryEditBatchComposer Handler(
        string marker, DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        return Composer(collector: new MarkerCollector(marker, severity));
    }

    /// <summary>
    ///     Validation must survive Edit mode being off. The panel's Validate button renders in every
    ///     mode and is the only source of story diagnostics there, so gating this on the editing flag
    ///     silently left read-only users with none - regression guard for exactly that. The handler
    ///     is built without a configuration provider, so it has no flag to read: a gate cannot come
    ///     back without this construction changing.
    /// </summary>
    [Fact]
    public async Task StoryEditingOff_StillValidates()
    {
        var result = await Handler("STORY_ELAPSED")
            .ValidateAsync(Subject, Staged(), CancellationToken.None);

        Assert.Null(result.Error);
        var diagnostic = Assert.Single(result.Problems);
        Assert.Equal(StoryGraphBuilder.EventNodeId(ThreadUri, "Start"), diagnostic.NodeId);
    }

    [Fact]
    public async Task NoBatch_DiagnosesCommittedText()
    {
        // STORY_ELAPSED is the committed trigger of "Start" - with no staged commands the validation
        // runs over buffer text and pins the marker to Start's node.
        var result = await Handler("STORY_ELAPSED").ValidateAsync(Subject, Staged(), CancellationToken.None);

        var diagnostic = Assert.Single(result.Problems);
        Assert.Equal(StoryGraphBuilder.EventNodeId(ThreadUri, "Start"), diagnostic.NodeId);
        Assert.Equal("error", diagnostic.Severity);
        Assert.Equal("marker 'STORY_ELAPSED'", diagnostic.Message);
        Assert.Equal(ThreadUri, diagnostic.Uri);
    }

    [Fact]
    public async Task NoBatch_OutsideAnyEvent_HasNoNodeId()
    {
        // The root tag opens the file before any event, so no event's range holds the marker: a
        // file-level problem, which the panel lists without a node.
        var result = await Handler("<Story>", severity: DiagnosticSeverity.Warning)
            .ValidateAsync(Subject, Staged(), CancellationToken.None);

        var diagnostic = Assert.Single(result.Problems);
        Assert.Null(diagnostic.NodeId);
        Assert.Equal("warning", diagnostic.Severity);
        Assert.Equal(ThreadUri, diagnostic.Uri);
    }

    [Fact]
    public async Task StagedEdit_DiagnosesPendingText_WithoutTouchingTheBuffer()
    {
        // The marker exists nowhere in the committed text - only the staged setDialog introduces it.
        // A diagnostic for it proves the collector ran over the composed (pending) text.
        var handler = Handler("ZZBAD");

        var empty = await handler.ValidateAsync(Subject, Staged(), CancellationToken.None);
        Assert.Empty(empty.Problems); // committed text has no marker

        var staged = await handler.ValidateAsync(Subject, Staged(Cmd("setDialog", eventName: "Start", value: "ZZBAD")),
            CancellationToken.None);

        var diagnostic = Assert.Single(staged.Problems);
        Assert.Equal(StoryGraphBuilder.EventNodeId(ThreadUri, "Start"), diagnostic.NodeId);
        Assert.Contains("ZZBAD", diagnostic.Message);
    }

    [Fact]
    public async Task StagedCommandThatFails_ShortCircuitsWithThatError()
    {
        var result = await Handler("anything")
            .ValidateAsync(Subject, Staged(Cmd("deleteEvent", eventName: "Ghost")), CancellationToken.None);

        Assert.Empty(result.Problems);
        Assert.Contains("Ghost", result.Error);
    }
}