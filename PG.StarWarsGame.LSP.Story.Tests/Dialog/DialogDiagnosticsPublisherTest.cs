// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Story.Dialog;
using PG.StarWarsGame.LSP.Story.Dialog.Handlers;

namespace PG.StarWarsGame.LSP.Story.Tests.Dialog;

public sealed class DialogDiagnosticsPublisherTest
{
    private const string InScopeUri = "file:///ws/data/scripts/story/dialog_test.txt";
    private const string OutOfScopeUri = "file:///ws/readme.txt";

    private static (DialogDiagnosticsPublisher Publisher,
        List<PublishDiagnosticsParams> Published,
        FakeWorkspaceHost Host,
        FakeIndexService IndexService) Build(FakeDialogScope? scope = null)
    {
        var published = new List<PublishDiagnosticsParams>();
        var host = new FakeWorkspaceHost();
        var indexService = new FakeIndexService();
        var fileHelper = new FileHelper(new MockFileSystem());
        var publisher = new DialogDiagnosticsPublisher(
            p => published.Add(p),
            indexService,
            host,
            scope ?? new FakeDialogScope(),
            new DialogFactProducer(new EmptySchemaProvider()),
            new DialogDiagnosticsHandlerRegistry([new UnknownDialogCommandHandler()]),
            fileHelper);
        return (publisher, published, host, indexService);
    }

    [Fact]
    public async Task RevalidateDocument_InScopeFile_PublishesDialogDiagnostics()
    {
        var (publisher, published, host, _) = Build();
        host.AddOrUpdate(InScopeUri, "[CHAPTER 0]\nBOGUS_COMMAND arg", 1);

        await publisher.RevalidateDocumentAsync(InScopeUri, CancellationToken.None);

        var diag = Assert.Single(Assert.Single(published).Diagnostics!);
        Assert.Contains("BOGUS_COMMAND", diag.Message);
        Assert.Equal("story-dialog", diag.Code!.Value.String);
    }

    [Fact]
    public async Task RevalidateDocument_ParseProblem_IsPublished()
    {
        var (publisher, published, host, _) = Build();
        host.AddOrUpdate(InScopeUri, "[CHAPTER X]", 1);

        await publisher.RevalidateDocumentAsync(InScopeUri, CancellationToken.None);

        var diag = Assert.Single(Assert.Single(published).Diagnostics!);
        Assert.Contains("chapter header", diag.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    [Fact]
    public async Task RevalidateDocument_OutOfScopeTxt_PublishesEmpty()
    {
        var (publisher, published, host, _) = Build();
        host.AddOrUpdate(OutOfScopeUri, "just some notes, not a dialog script", 1);

        await publisher.RevalidateDocumentAsync(OutOfScopeUri, CancellationToken.None);

        Assert.Empty(Assert.Single(published).Diagnostics!);
    }

    [Fact]
    public async Task RevalidateDocument_ScopeDisabled_PublishesNothing()
    {
        var (publisher, published, host, _) = Build(new FakeDialogScope { Enabled = false });
        host.AddOrUpdate(InScopeUri, "[CHAPTER 0]\nBOGUS", 1);

        await publisher.RevalidateDocumentAsync(InScopeUri, CancellationToken.None);

        Assert.Empty(published);
    }

    [Fact]
    public async Task ClearDocument_PublishesEmptyDiagnostics()
    {
        var (publisher, published, host, _) = Build();
        host.AddOrUpdate(InScopeUri, "[CHAPTER 0]\nBOGUS", 1);
        await publisher.RevalidateDocumentAsync(InScopeUri, CancellationToken.None);

        publisher.ClearDocument(InScopeUri);

        Assert.Equal(2, published.Count);
        Assert.Empty(published[1].Diagnostics!);
    }

    [Fact]
    public void IndexChanged_RerunsOpenDialogDocuments()
    {
        // Localisation or symbol updates arrive as index changes; open dialog docs re-validate.
        var (_, published, host, indexService) = Build();
        host.AddOrUpdate(InScopeUri, "[CHAPTER 0]\nBOGUS", 1);

        indexService.Fire(GameIndex.Empty);

        var diag = Assert.Single(Assert.Single(published).Diagnostics!);
        Assert.Contains("BOGUS", diag.Message);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    internal sealed class FakeDialogScope : IStoryDialogScope
    {
        public bool Enabled { get; init; } = true;

        public bool IsInScope(string canonicalUri)
        {
            return canonicalUri.Contains("/data/scripts/story/", StringComparison.OrdinalIgnoreCase);
        }

        public string? ResolveDialogFile(string dialogName)
        {
            return null;
        }

        public IReadOnlyCollection<int> GetChapters(string canonicalUri)
        {
            return [];
        }
    }

    internal sealed class FakeWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = new(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<TrackedDocument> All => _docs.Values;

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
    }

    internal sealed class FakeIndexService : IGameIndexService
    {
        public GameIndex Current => GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;

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

        public void Fire(GameIndex index)
        {
            IndexChanged?.Invoke(index);
        }

        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task OpenDocumentAsync(string uri, string text, int version, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void InjectDocument(DocumentIndex document)
        {
        }

        public void RemoveDocument(string uri)
        {
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
            System.Collections.Immutable.ImmutableDictionary<string, System.Collections.Immutable.ImmutableArray<string>> bones)
        {
        }

        public void ApplyWorkspaceDynamicEnumValues(
            System.Collections.Immutable.ImmutableDictionary<string, System.Collections.Immutable.ImmutableArray<string>> values)
        {
        }

        public void ApplyWorkspaceEnumValueDefinitions(
            System.Collections.Immutable.ImmutableDictionary<string,
                System.Collections.Immutable.ImmutableDictionary<string, FileOrigin>> definitions)
        {
        }

        public IDisposable BeginBulkUpdate()
        {
            return new Noop();
        }

        private sealed class Noop : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class EmptySchemaProvider : ISchemaProvider
    {
        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

        public XmlTagDefinition? GetTag(string t)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string t)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string t)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string e)
        {
            return null;
        }

        public GameObjectTypeDefinition? GetObjectType(string t)
        {
            return null;
        }
    }
}
