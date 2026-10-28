// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Lua.Tests;

public sealed class LuaTextDocumentSyncHandlerTest
{
    private const string DiskUri = "file:///c:/scripts/test.lua";
    private const string DiskPath = @"c:\scripts\test.lua";
    private const string DiskContent = "function Definitions() end";
    private const string XmlDiskUri = "file:///c:/data/xml/units.xml";
    private const string XmlDiskPath = @"c:\data\xml\units.xml";
    private static DocumentUri TestUri => DocumentUri.From("file:///test.lua");

    private static (LuaTextDocumentSyncHandler handler,
        FakeGameWorkspaceHost host,
        FakeGameIndexService index) Build(MockFileSystem? fs = null)
    {
        var host = new FakeGameWorkspaceHost();
        var index = new FakeGameIndexService();
        var fileHelper = new FileHelper(fs ?? new MockFileSystem());
        // An already-open gate runs handler actions immediately (normal post-startup operation).
        var gate = new StartupGate();
        // A fresh gate holds nothing to pump, so opening it completes synchronously; a constructor cannot await.
#pragma warning disable VSTHRD002
        gate.OpenAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        return (new LuaTextDocumentSyncHandler(host, index, fileHelper, gate, new RecordingLuaAnalyzer()), host, index);
    }

    // ── analyzer mirror ──────────────────────────────────────────────────────

    [Fact]
    public async Task OpenChangeAndClose_AreMirroredToTheAnalyzer()
    {
        var analyzer = new RecordingLuaAnalyzer();
        var gate = new StartupGate();
        await gate.OpenAsync();
        var handler = new LuaTextDocumentSyncHandler(new FakeGameWorkspaceHost(), new FakeGameIndexService(),
            new FileHelper(new MockFileSystem()), gate, analyzer);

        await handler.Handle(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = TestUri, Text = "x = 1", LanguageId = "lua", Version = 1 }
        }, CancellationToken.None);
        await handler.Handle(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = TestUri, Version = 2 },
            ContentChanges =
                new Container<TextDocumentContentChangeEvent>(new TextDocumentContentChangeEvent { Text = "x = 2" })
        }, CancellationToken.None);
        await handler.Handle(new DidCloseTextDocumentParams { TextDocument = new TextDocumentIdentifier(TestUri) },
            CancellationToken.None);

        Assert.Equal([$"open {TestUri} v1 x = 1", $"change {TestUri} v2 x = 2", $"close {TestUri}"], analyzer.Calls);
    }

    // ── DidOpen ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DidOpen_Adds_Document_To_WorkspaceHost()
    {
        var (handler, host, _) = Build();

        await handler.Handle(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
                { Uri = TestUri, Text = "function Foo() end", LanguageId = "lua", Version = 1 }
        }, CancellationToken.None);

        Assert.Single(host.Writes);
        Assert.Equal(TestUri.ToString(), host.Writes[0].Uri);
        Assert.Equal("function Foo() end", host.Writes[0].Text);
        Assert.Equal(1, host.Writes[0].Version);
    }

    [Fact]
    public async Task DidOpen_Triggers_Index_Open()
    {
        // didOpen must route through OpenDocumentAsync (version-epoch reset), not
        // UpdateDocumentAsync - a fresh session's client versions restart at 1 and would
        // otherwise be dropped as stale against the previous session's committed version.
        var (handler, _, index) = Build();

        await handler.Handle(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
                { Uri = TestUri, Text = "function Foo() end", LanguageId = "lua", Version = 1 }
        }, CancellationToken.None);

        Assert.Empty(index.Updates);
        Assert.Single(index.Opens);
        Assert.Equal(TestUri.ToString(), index.Opens[0].Uri);
        Assert.Equal("function Foo() end", index.Opens[0].Text);
        Assert.Equal(1, index.Opens[0].Version);
    }

    // ── DidChange ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DidChange_Updates_WorkspaceHost_With_New_Text()
    {
        var (handler, host, _) = Build();

        await handler.Handle(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier
                { Uri = TestUri, Version = 2 },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(
                new TextDocumentContentChangeEvent { Text = "function Bar() end" })
        }, CancellationToken.None);

        Assert.Single(host.Writes);
        Assert.Equal("function Bar() end", host.Writes[0].Text);
        Assert.Equal(2, host.Writes[0].Version);
    }

    [Fact]
    public async Task DidChange_Triggers_Index_Update()
    {
        var (handler, _, index) = Build();

        await handler.Handle(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier
                { Uri = TestUri, Version = 3 },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(
                new TextDocumentContentChangeEvent { Text = "function Changed() end" })
        }, CancellationToken.None);

        Assert.Single(index.Updates);
        Assert.Equal("function Changed() end", index.Updates[0].Text);
        Assert.Equal(3, index.Updates[0].Version);
    }

    [Fact]
    public async Task DidChange_EmptyContentChanges_Uses_EmptyString()
    {
        var (handler, host, _) = Build();

        await handler.Handle(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = TestUri },
            ContentChanges = new Container<TextDocumentContentChangeEvent>()
        }, CancellationToken.None);

        Assert.Equal(string.Empty, host.Writes[0].Text);
    }

    // ── DidClose ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DidClose_Removes_Document_From_WorkspaceHost()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [DiskPath] = new(DiskContent) });
        var (handler, host, _) = Build(fs);

        await handler.Handle(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(DiskUri) }
        }, CancellationToken.None);

        Assert.Single(host.Removals);
        Assert.Equal(DiskUri, host.Removals[0]);
    }

    [Fact]
    public async Task DidClose_WhenFileExistsOnDisk_ReindexesWithoutRemoval()
    {
        // Mirrors XmlTextDocumentSyncHandler: the index entry must never be removed for a file
        // that still exists - a queued removal racing the async re-add can otherwise strip the
        // document's symbols permanently (the 2026-07-05 go-to-definition bug).
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [DiskPath] = new(DiskContent) });
        var (handler, _, index) = Build(fs);

        await handler.Handle(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(DiskUri) }
        }, CancellationToken.None);

        Assert.Empty(index.Removals);
        Assert.Single(index.Updates);
        Assert.Equal(DiskContent, index.Updates[0].Text);
        Assert.Equal(0, index.Updates[0].Version); // Current is empty → falls back to 0
        Assert.Equal(0, index.BulkUpdates);
    }

    [Fact]
    public async Task DidClose_WhenFileNotOnDisk_RemovesFromIndex()
    {
        var (handler, _, index) = Build(new MockFileSystem());

        await handler.Handle(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(DiskUri) }
        }, CancellationToken.None);

        Assert.Single(index.Removals);
        Assert.Empty(index.Updates);
    }

    [Fact]
    public async Task DidClose_WhenFileExistsOnDisk_DoesNotRetainHostEntry()
    {
        // The host tracks only open documents - after close the text lives on disk and every
        // closed-file consumer reads it from there on demand.
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [DiskPath] = new(DiskContent) });
        var (handler, host, _) = Build(fs);

        await handler.Handle(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(DiskUri) }
        }, CancellationToken.None);

        Assert.Single(host.Removals);
        Assert.Empty(host.Writes);
    }

    // ── Non-Lua documents (dual-registration crosstalk guard) ────────────────
    // OmniSharp routes didClose/didChange by tracked attributes, not language id, so this
    // handler also receives notifications for XML documents. It must ignore them entirely:
    // a Lua-side removal racing the XML handler's own re-add silently deleted XML documents
    // from the index (the 2026-07-05 "go-to resolves to base game after closing a tab" bug).

    [Fact]
    public async Task DidOpen_NonLuaDocument_IsIgnored()
    {
        var (handler, host, index) = Build();

        await handler.Handle(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
                { Uri = DocumentUri.From(XmlDiskUri), Text = "<Foo/>", LanguageId = "xml", Version = 1 }
        }, CancellationToken.None);

        Assert.Empty(host.Writes);
        Assert.Empty(index.Updates);
        Assert.Empty(index.Opens);
    }

    [Fact]
    public async Task DidChange_NonLuaDocument_IsIgnored()
    {
        var (handler, host, index) = Build();

        await handler.Handle(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier
                { Uri = DocumentUri.From(XmlDiskUri), Version = 2 },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(
                new TextDocumentContentChangeEvent { Text = "<Bar/>" })
        }, CancellationToken.None);

        Assert.Empty(host.Writes);
        Assert.Empty(index.Updates);
    }

    [Fact]
    public async Task DidClose_NonLuaDocument_IsIgnored()
    {
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [XmlDiskPath] = new("<Foo/>") });
        var (handler, host, index) = Build(fs);

        await handler.Handle(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(XmlDiskUri) }
        }, CancellationToken.None);

        Assert.Empty(host.Removals);
        Assert.Empty(index.Removals);
        Assert.Empty(index.Updates);
    }

    // ── DidSave ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DidSave_Does_Not_Touch_Host_Or_Index()
    {
        var (handler, host, index) = Build();

        await handler.Handle(new DidSaveTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = TestUri }
        }, CancellationToken.None);

        Assert.Empty(host.Writes);
        Assert.Empty(index.Updates);
    }

    // ── GetTextDocumentAttributes ─────────────────────────────────────────────

    [Fact]
    public void GetTextDocumentAttributes_ReturnsLuaLanguage()
    {
        var (handler, _, _) = Build();
        var attrs = handler.GetTextDocumentAttributes(TestUri);
        Assert.Equal("lua", attrs.LanguageId);
    }

    // OmniSharp asks every sync handler what a document is and routes the open to the handler
    // whose selector matches the answer it takes. A handler claiming its own language for every
    // URI made an XML open land here on some server starts, where it was ignored.
    [Theory]
    [InlineData("file:///d%3A/mod/data/xml/story_plot.xml", "xml")]
    [InlineData("file:///d%3A/mod/data/text/dialog.txt", "plaintext")]
    [InlineData("file:///d%3A/mod/data/scripts/story/plot.lua", "lua")]
    public void GetTextDocumentAttributes_AnswersByExtension_NotByOwner(string uri, string language)
    {
        var (handler, _, _) = Build();
        Assert.Equal(language, handler.GetTextDocumentAttributes(DocumentUri.From(uri)).LanguageId);
    }

    private sealed class RecordingLuaAnalyzer : ILuaAnalyzer
    {
        public List<string> Calls { get; } = [];

        public bool IsRunning => true;

        public event Action<PublishDiagnosticsParams>? DiagnosticsPublished
        {
            add { }
            remove { }
        }

        public void DidOpen(string uri, string text, int version)
        {
            Calls.Add($"open {uri} v{version} {text}");
        }

        public void DidChange(string uri, string text, int version)
        {
            Calls.Add($"change {uri} v{version} {text}");
        }

        public Task<T?> RequestAsync<T>(string method, object parameters, CancellationToken ct) where T : class?
        {
            return Task.FromResult<T?>(null);
        }

        public void DidClose(string uri)
        {
            Calls.Add($"close {uri}");
        }
    }

    // ── Fakes ────────────────────────────────────────────────────────────────
}