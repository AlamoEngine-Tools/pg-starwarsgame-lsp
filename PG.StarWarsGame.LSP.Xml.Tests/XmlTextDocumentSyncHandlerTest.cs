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
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlTextDocumentSyncHandlerTest
{
    // Absolute-path URI so FileUriToPath produces a real path the MockFileSystem can check.
    private const string DiskUri = "file:///c:/data/test.xml";
    private const string DiskPath = @"c:\data\test.xml";
    private const string DiskContent = "<DiskContent/>";
    private static DocumentUri TestUri => DocumentUri.From("file:///test.xml");

    private static (XmlTextDocumentSyncHandler handler,
        FakeGameWorkspaceHost host,
        FakeGameIndexService index) Build(MockFileSystem? fs = null, IEaWXmlContext? ctx = null,
            IStartupGate? gate = null)
    {
        var host = new FakeGameWorkspaceHost();
        var index = new FakeGameIndexService();
        var fileHelper = new FileHelper(fs ?? new MockFileSystem());
        return (new XmlTextDocumentSyncHandler(host, index, fileHelper,
                ctx ?? new AllowAllEaWContext(), gate ?? OpenGate()),
            host, index);
    }

    // A gate that runs handler actions immediately (normal post-startup operation).
    private static StartupGate OpenGate()
    {
        var gate = new StartupGate();
        gate.OpenAsync().GetAwaiter().GetResult();
        return gate;
    }

    // ── DidOpen ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DidOpen_Adds_Document_To_WorkspaceHost()
    {
        var (handler, host, _) = Build();

        await handler.Handle(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
                { Uri = TestUri, Text = "<Foo/>", LanguageId = "xml", Version = 1 }
        }, CancellationToken.None);

        Assert.Single(host.AddOrUpdateCalls);
        Assert.Equal(TestUri.ToString(), host.AddOrUpdateCalls[0].Uri);
        Assert.Equal("<Foo/>", host.AddOrUpdateCalls[0].Text);
        Assert.Equal(1, host.AddOrUpdateCalls[0].Version);
    }

    [Fact]
    public async Task DidOpen_Triggers_Index_Open()
    {
        // didOpen must route through OpenDocumentAsync (version-epoch reset), not
        // UpdateDocumentAsync — a fresh session's client versions restart at 1 and would
        // otherwise be dropped as stale against the previous session's committed version.
        var (handler, _, index) = Build();

        await handler.Handle(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
                { Uri = TestUri, Text = "<Foo/>", LanguageId = "xml", Version = 1 }
        }, CancellationToken.None);

        Assert.Empty(index.UpdateCalls);
        Assert.Single(index.OpenCalls);
        Assert.Equal(TestUri.ToString(), index.OpenCalls[0].Uri);
        Assert.Equal("<Foo/>", index.OpenCalls[0].Text);
        Assert.Equal(1, index.OpenCalls[0].Version);
    }

    [Fact]
    public async Task DidOpen_WhileGateClosed_IsBuffered_ThenAppliedOnOpen()
    {
        // While the startup pipeline runs the gate is closed: the open is buffered and applied
        // only when the gate drains, by which point the index and EaW directories are ready.
        var gate = new StartupGate(); // closed
        var (handler, host, index) = Build(gate: gate);

        await handler.Handle(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
                { Uri = TestUri, Text = "<Foo/>", LanguageId = "xml", Version = 1 }
        }, CancellationToken.None);

        Assert.Empty(host.AddOrUpdateCalls);
        Assert.Empty(index.OpenCalls);

        await gate.OpenAsync();

        Assert.Single(host.AddOrUpdateCalls);
        Assert.Single(index.OpenCalls);
        Assert.Equal("<Foo/>", index.OpenCalls[0].Text);
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
                new TextDocumentContentChangeEvent { Text = "<New/>" })
        }, CancellationToken.None);

        Assert.Single(host.AddOrUpdateCalls);
        Assert.Equal("<New/>", host.AddOrUpdateCalls[0].Text);
        Assert.Equal(2, host.AddOrUpdateCalls[0].Version);
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
                new TextDocumentContentChangeEvent { Text = "<Changed/>" })
        }, CancellationToken.None);

        Assert.Single(index.UpdateCalls);
        Assert.Equal("<Changed/>", index.UpdateCalls[0].Text);
        Assert.Equal(3, index.UpdateCalls[0].Version);
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

        Assert.Equal(string.Empty, host.AddOrUpdateCalls[0].Text);
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

        Assert.Single(host.RemoveCalls);
        Assert.Equal(DiskUri, host.RemoveCalls[0]);
    }

    [Fact]
    public async Task DidClose_WhenFileExistsOnDisk_RestoresDiskContentWithoutRemoveOrBulk()
    {
        // File exists on disk — close restores the on-disk state via a single UpdateDocument so
        // cross-file references keep resolving. It no longer removes-then-re-adds (which dropped the
        // document's symbols mid-flight) nor wraps the work in a bulk update; the index's own
        // unchanged-content fast path makes the common (unedited) case a cheap no-op.
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [DiskPath] = new(DiskContent) });
        var (handler, _, index) = Build(fs);

        await handler.Handle(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(DiskUri) }
        }, CancellationToken.None);

        Assert.Empty(index.RemoveCalls);
        Assert.Single(index.UpdateCalls);
        Assert.Equal(DiskContent, index.UpdateCalls[0].Text);
        Assert.Equal(0, index.BulkUpdateCount);
    }

    [Fact]
    public async Task DidClose_WhenFileNotOnDisk_RemovesFromIndex()
    {
        // File doesn't exist on disk (was deleted) — fully remove from index.
        var (handler, _, index) = Build(new MockFileSystem());

        await handler.Handle(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(DiskUri) }
        }, CancellationToken.None);

        Assert.Single(index.RemoveCalls);
        Assert.Empty(index.UpdateCalls);
    }

    [Fact]
    public async Task DidClose_WhenFileExistsOnDisk_DoesNotRetainHostEntry()
    {
        // The host tracks only open documents — after close the text lives on disk and every
        // closed-file consumer reads it from there on demand.
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
            { [DiskPath] = new(DiskContent) });
        var (handler, host, _) = Build(fs);

        await handler.Handle(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(DiskUri) }
        }, CancellationToken.None);

        Assert.Single(host.RemoveCalls);
        Assert.Empty(host.AddOrUpdateCalls);
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

        Assert.Empty(host.AddOrUpdateCalls);
        Assert.Empty(index.UpdateCalls);
    }

    // ── EaW directory gating ─────────────────────────────────────────────────

    [Fact]
    public async Task DidOpen_NonEaWFile_DoesNotAddToWorkspaceHost()
    {
        var (handler, host, index) = Build(ctx: new DenyAllEaWContext());

        await handler.Handle(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
                { Uri = TestUri, Text = "<Foo/>", LanguageId = "xml", Version = 1 }
        }, CancellationToken.None);

        Assert.Empty(host.AddOrUpdateCalls);
        Assert.Empty(index.UpdateCalls);
        Assert.Empty(index.OpenCalls);
    }

    [Fact]
    public async Task DidChange_NonEaWFile_DoesNotUpdateWorkspaceHost()
    {
        var (handler, host, index) = Build(ctx: new DenyAllEaWContext());

        await handler.Handle(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = TestUri },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(
                new TextDocumentContentChangeEvent { Text = "<Changed/>" })
        }, CancellationToken.None);

        Assert.Empty(host.AddOrUpdateCalls);
        Assert.Empty(index.UpdateCalls);
    }

    [Fact]
    public async Task DidClose_NonEaWFile_DoesNotRemoveFromWorkspaceHost()
    {
        var (handler, host, index) = Build(ctx: new DenyAllEaWContext());

        await handler.Handle(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = TestUri }
        }, CancellationToken.None);

        Assert.Empty(host.RemoveCalls);
        Assert.Empty(index.RemoveCalls);
        Assert.Empty(index.UpdateCalls);
    }

    // ── GetTextDocumentAttributes ─────────────────────────────────────────────

    [Fact]
    public void GetTextDocumentAttributes_ReturnsXmlLanguage()
    {
        var (handler, _, _) = Build();
        var attrs = handler.GetTextDocumentAttributes(TestUri);
        Assert.Equal("xml", attrs.LanguageId);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    internal sealed class FakeGameWorkspaceHost : IGameWorkspaceHost
    {
        public List<Call> AddOrUpdateCalls { get; } = [];
        public List<string> RemoveCalls { get; } = [];

        public void AddOrUpdate(string uri, string text, int version, bool publishDiagnostics = true)
        {
            AddOrUpdateCalls.Add(new Call(uri, text, version, publishDiagnostics));
        }

        public void Remove(string uri)
        {
            RemoveCalls.Add(uri);
        }

        public bool TryGet(string uri, out TrackedDocument doc)
        {
            doc = null!;
            return false;
        }

        public IEnumerable<TrackedDocument> All => [];

        public record Call(string Uri, string Text, int Version, bool PublishDiagnostics = true);
    }

    internal sealed class FakeGameIndexService : IGameIndexService
    {
        public List<UpdateCall> UpdateCalls { get; } = [];
        public List<UpdateCall> OpenCalls { get; } = [];
        public List<string> RemoveCalls { get; } = [];
        public int BulkUpdateCount { get; private set; }

        public GameIndex Current => GameIndex.Empty;

        public event Action<GameIndex>? IndexChanged
        {
            add { }
            remove { }
        }

        public event Action<ILocalisationIndex>? LocalisationChanged
        {
            add { }
            remove { }
        }

        public event Action<GameIndex>? DynamicEnumChanged
        {
            add { }
            remove { }
        }

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            UpdateCalls.Add(new UpdateCall(uri, text, version));
            return Task.CompletedTask;
        }

        public Task OpenDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            OpenCalls.Add(new UpdateCall(uri, text, version));
            return Task.CompletedTask;
        }

        public void InjectDocument(DocumentIndex document)
        {
        }

        public void RemoveDocument(string uri)
        {
            RemoveCalls.Add(uri);
        }

        public void ApplyBaseline(BaselineIndex baseline)
        {
        }

        public void ApplyLocalisation(ILocalisationIndex index)
        {
        }

        public void ApplyAssetFiles(IAssetFileIndex index)
        {
        }

        public void ApplyModelBones(
            ImmutableDictionary<string, ImmutableArray<string>> bones)
        {
        }
        public void ApplyWorkspaceDynamicEnumValues(ImmutableDictionary<string, ImmutableArray<string>> values)
        {
        }
        public void ApplyWorkspaceEnumValueDefinitions(
            ImmutableDictionary<string, ImmutableDictionary<string, FileOrigin>> definitions)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            BulkUpdateCount++;
            return NullDisposable.Instance;
        }

        public record UpdateCall(string Uri, string Text, int Version);

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
