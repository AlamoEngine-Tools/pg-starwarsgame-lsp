// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

public sealed class StoryDialogReferenceHandlerTest
{
    private const string DialogUri = "file:///ws/data/scripts/story/dialog_mission.txt";

    private static IReadOnlyList<XmlDiagnosticResult> Handle(StoryDialogRefFact fact, FakeStoryDialogScope scope)
    {
        var ctx = new DiagnosticsContext(new EmptySchemaProvider(), GameIndex.Empty, fact.DocumentUri, "en");
        IXmlDiagnosticsHandler handler = new StoryDialogReferenceHandler(scope);
        return handler.Handle(fact, ctx).ToList();
    }

    private static StoryDialogRefFact Fact(string dialogName = "dialog_mission",
        int? chapter = null, int chapterLine = -1)
    {
        return new StoryDialogRefFact("file:///ws/data/xml/story_a.xml", 5, 0, 0,
            dialogName, chapter, chapterLine);
    }

    [Fact]
    public void ScopeDisabled_ProducesNothing()
    {
        var scope = new FakeStoryDialogScope { Enabled = false };

        Assert.Empty(Handle(Fact(), scope));
    }

    [Fact]
    public void UnresolvedDialogName_ProducesWarningOnEvent()
    {
        var scope = new FakeStoryDialogScope();

        var result = Assert.Single(Handle(Fact("dialog_gone"), scope));
        Assert.Equal(XmlDiagnosticSeverity.Warning, result.Severity);
        Assert.Contains("dialog_gone", result.Message);
        Assert.Contains("storyDialog", result.Message);
    }

    [Fact]
    public void ResolvedDialog_NoChapterTag_ProducesNothing()
    {
        var scope = new FakeStoryDialogScope { Files = { ["dialog_mission"] = DialogUri }, Chapters = [0] };

        Assert.Empty(Handle(Fact(), scope));
    }

    [Fact]
    public void ResolvedDialog_DefinedChapter_ProducesNothing()
    {
        var scope = new FakeStoryDialogScope { Files = { ["dialog_mission"] = DialogUri }, Chapters = [0, 2] };

        Assert.Empty(Handle(Fact(chapter: 2, chapterLine: 6), scope));
    }

    [Fact]
    public void ResolvedDialog_UndefinedChapter_ProducesErrorAtChapterLine()
    {
        var scope = new FakeStoryDialogScope { Files = { ["dialog_mission"] = DialogUri }, Chapters = [0, 1] };

        var result = Assert.Single(Handle(Fact(chapter: 4, chapterLine: 6), scope));
        Assert.Equal(XmlDiagnosticSeverity.Error, result.Severity);
        Assert.Contains("Chapter 4", result.Message);
        Assert.Contains("0, 1", result.Message);
        Assert.Equal(6, result.OverrideLine);
    }

    [Fact]
    public void ResolvedDialog_NoChaptersDefinedAtAll_SaysSo()
    {
        var scope = new FakeStoryDialogScope { Files = { ["dialog_mission"] = DialogUri } };

        var result = Assert.Single(Handle(Fact(chapter: 0, chapterLine: 6), scope));
        Assert.Contains("no chapters", result.Message);
    }

    private sealed class FakeStoryDialogScope : IStoryDialogScope
    {
        public bool Enabled { get; init; } = true;
        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<int> Chapters { get; init; } = [];

        public bool IsInScope(string canonicalUri)
        {
            return Files.ContainsValue(canonicalUri);
        }

        public string? ResolveDialogFile(string dialogName)
        {
            return Files.GetValueOrDefault(dialogName);
        }

        public IReadOnlyCollection<int> GetChapters(string canonicalUri)
        {
            return Chapters;
        }
    }
}
