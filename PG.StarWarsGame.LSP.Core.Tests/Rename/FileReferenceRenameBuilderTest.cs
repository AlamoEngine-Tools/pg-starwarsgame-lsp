// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Rename;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Tests.Rename;

public sealed class FileReferenceRenameBuilderTest
{
    private const string Dir = "file:///mods/mymod/data/xml/";
    private const string ManifestUri = Dir + "story_plots_rebel.xml";
    private const string CampaignUri = Dir + "campaigns.xml";
    private const string LuaDir = "file:///mods/mymod/data/scripts/story/";
    private const string ScriptUri = LuaDir + "story_campaign.lua";
    private const string ManifestKey = "storyplotmanifest:story_plots_rebel.xml";
    private const string LuaKey = "luascript:story_campaign";

    private static GameIndex IndexWith(string id, string fileUri, string typeName,
        params GameReference[] refs)
    {
        var sym = new GameSymbol(id, GameSymbolKind.WorkspaceFile, typeName, new FileOrigin(fileUri, 0, 0), null);
        return GameIndex.Empty with
        {
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add(id, [sym]),
            WorkspaceReferences = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add(id, [.. refs])
        };
    }

    private static GameReference Ref(string uri, int line, int column, int length)
    {
        return new GameReference(ManifestKey, GameSymbolKind.WorkspaceFile, "StoryPlotManifest",
            uri, line, column, length);
    }

    private static (RenameFile? File, IReadOnlyList<(string Uri, TextEdit Edit)> Edits) Decompose(WorkspaceEdit edit)
    {
        var changes = edit.DocumentChanges!.ToList();
        var rename = changes.Where(c => c.IsRenameFile).Select(c => c.RenameFile!).FirstOrDefault();
        var edits = changes.Where(c => c.IsTextDocumentEdit)
            .SelectMany(c => c.TextDocumentEdit!.Edits.Select(e => (c.TextDocumentEdit!.TextDocument.Uri.ToString(), e)))
            .ToList();
        return (rename, edits);
    }

    // ── plot manifest rename ──────────────────────────────────────────────────

    [Fact]
    public void Build_RenamesFileOnDisk_KeepingDirectoryAndExtension()
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(CampaignUri,
            "<Campaign>\n<Empire_Story_Name>Story_Plots_Rebel.xml</Empire_Story_Name>\n</Campaign>", 1);
        var index = IndexWith(ManifestKey, ManifestUri, "StoryPlotManifest",
            Ref(CampaignUri, 1, "<Empire_Story_Name>".Length, "Story_Plots_Rebel.xml".Length));

        var result = FileReferenceRenameBuilder.Build(ManifestKey, "Story_Plots_Rebel2",
            index, Source(host), NullLogger.Instance);

        Assert.NotNull(result);
        var (file, edits) = Decompose(result!);
        Assert.NotNull(file);
        Assert.Equal(ManifestUri, file!.OldUri.ToString());
        Assert.Equal(Dir + "Story_Plots_Rebel2.xml", file.NewUri.ToString());

        var (_, edit) = Assert.Single(edits);
        Assert.Equal("Story_Plots_Rebel2", edit.NewText);
        // Only the stem is replaced, not the ".xml" extension.
        Assert.Equal("<Empire_Story_Name>".Length, edit.Range.Start.Character);
        Assert.Equal("<Empire_Story_Name>Story_Plots_Rebel".Length, edit.Range.End.Character);
    }

    [Fact]
    public void Build_SubdirReference_PreservesDirectoryPrefixAndExtension()
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(CampaignUri,
            "<Campaign>\n<Story_Name>Rebel, Conquests\\Story_Plots_Rebel.xml</Story_Name>\n</Campaign>", 1);
        var index = IndexWith(ManifestKey, ManifestUri, "StoryPlotManifest",
            Ref(CampaignUri, 1, "<Story_Name>Rebel, ".Length, "Conquests\\Story_Plots_Rebel.xml".Length));

        var result = FileReferenceRenameBuilder.Build(ManifestKey, "Story_Plots_Rebel2",
            index, Source(host), NullLogger.Instance);

        var (_, edits) = Decompose(result!);
        var (_, edit) = Assert.Single(edits);
        Assert.Equal("Story_Plots_Rebel2", edit.NewText);
        // Edit starts after "Conquests\" and ends before ".xml".
        Assert.Equal("<Story_Name>Rebel, Conquests\\".Length, edit.Range.Start.Character);
        Assert.Equal("<Story_Name>Rebel, Conquests\\Story_Plots_Rebel".Length, edit.Range.End.Character);
    }

    // ── lua script rename ─────────────────────────────────────────────────────

    [Fact]
    public void Build_LuaScript_RenamesExtensionlessTokenAndDotLuaFile()
    {
        var host = new FakeWorkspaceHost();
        host.AddOrUpdate(CampaignUri,
            "<Story_Mode_Plots>\n<Lua_Script>Story_Campaign</Lua_Script>\n</Story_Mode_Plots>", 1);
        var luaRef = new GameReference(LuaKey, GameSymbolKind.WorkspaceFile, "LuaScript",
            CampaignUri, 1, "<Lua_Script>".Length, "Story_Campaign".Length);
        var index = IndexWith(LuaKey, ScriptUri, "LuaScript", luaRef);

        var result = FileReferenceRenameBuilder.Build(LuaKey, "Story_Campaign2",
            index, Source(host), NullLogger.Instance);

        Assert.NotNull(result);
        var (file, edits) = Decompose(result!);
        Assert.Equal(LuaDir + "Story_Campaign2.lua", file!.NewUri.ToString());
        var (_, edit) = Assert.Single(edits);
        Assert.Equal("Story_Campaign2", edit.NewText);
        Assert.Equal("<Lua_Script>".Length, edit.Range.Start.Character);
        Assert.Equal("<Lua_Script>Story_Campaign".Length, edit.Range.End.Character);
    }

    // ── guards ────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_NewNameWithDirectorySeparator_ReturnsNull()
    {
        var index = IndexWith(ManifestKey, ManifestUri, "StoryPlotManifest");
        Assert.Null(FileReferenceRenameBuilder.Build(ManifestKey, "Foo/Bar",
            index, Source(new FakeWorkspaceHost()), NullLogger.Instance));
    }

    [Theory]
    [InlineData("Foo:Bar")]  // drive/stream separator
    [InlineData("Foo*Bar")]  // wildcard
    [InlineData("Foo?Bar")]  // wildcard
    [InlineData("Foo\"Bar")] // quote
    [InlineData("Foo<Bar")]
    [InlineData("Foo>Bar")]
    [InlineData("Foo|Bar")]  // pipe
    [InlineData("Foo\tBar")] // control character
    [InlineData("Foo.")]     // trailing dot (Windows strips it → on-disk name desyncs)
    [InlineData("Foo ")]     // trailing space (same)
    public void Build_NewNameWithInvalidFileNameChar_ReturnsNull(string newStem)
    {
        var index = IndexWith(ManifestKey, ManifestUri, "StoryPlotManifest");
        Assert.Null(FileReferenceRenameBuilder.Build(ManifestKey, newStem,
            index, Source(new FakeWorkspaceHost()), NullLogger.Instance));
    }

    [Fact]
    public void Build_UnknownId_ReturnsNull()
    {
        Assert.Null(FileReferenceRenameBuilder.Build("storyplotmanifest:nope.xml", "X",
            GameIndex.Empty, Source(new FakeWorkspaceHost()), NullLogger.Instance));
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private static DocumentTextSource Source(IGameWorkspaceHost host)
    {
        return new DocumentTextSource(host, new FileHelper(new MockFileSystem()),
            NullLogger<DocumentTextSource>.Instance);
    }

    private sealed class FakeWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = [];

        public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
        {
            _docs[uri] = new TrackedDocument(uri, text, version, publishDiagnostics);
        }

        public void Remove(string uri)
        {
            _docs.Remove(uri);
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            return _docs.TryGetValue(uri, out doc!);
        }

        public IEnumerable<TrackedDocument> All => _docs.Values;
    }
}
