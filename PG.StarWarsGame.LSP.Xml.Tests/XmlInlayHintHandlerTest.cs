// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.InlayHints;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlInlayHintHandlerTest
{
    private const string Uri = "file:///data/units.xml";

    private static (XmlInlayHintHandler handler, FakeIndexService index) Build(
        string docText,
        ISchemaProvider? schema = null,
        ILocalisationIndex? localisation = null)
    {
        var host = new FakeWorkspaceHost();
        host.Set(Uri, docText);

        var index = new FakeIndexService();
        if (localisation is not null)
            index.Localisation = localisation;

        var registry = new XmlInlayHintRegistry([new LocalisationKeySingleValueInlayHintProvider()]);
        return (new XmlInlayHintHandler(
            TestParseCache.For(host),
            index,
            schema ?? new EmptySchemaProvider(),
            new AllowAllEaWContext(),
            new FileHelper(new MockFileSystem()),
            NullLogger<XmlInlayHintHandler>.Instance,
            registry), index);
    }

    private static LspRange FullRange()
    {
        return new LspRange(new Position(0, 0), new Position(int.MaxValue, 0));
    }

    private static InlayHintParams Params(LspRange? range = null)
    {
        return new InlayHintParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(Uri) },
            Range = range ?? FullRange()
        };
    }

    // ── basic hint production ────────────────────────────────────────────────

    [Fact]
    public async Task LocKeyTag_WithKnownTranslation_ProducesHintOnSameLine()
    {
        const string xml = "<GameObjectType>\n  <Text_ID>TEXT_UNIT_NAME</Text_ID>\n</GameObjectType>";
        var schema = new LocKeySchemaProvider("Text_ID");
        var loc = new ValueLocalisationIndex("TEXT_UNIT_NAME", "X-Wing Fighter");

        var (handler, _) = Build(xml, schema, loc);
        var result = await handler.Handle(Params(), CancellationToken.None);

        var hint = Assert.Single(result!);
        Assert.Equal(1, hint.Position.Line); // line 1 (0-based)
        Assert.Contains("X-Wing Fighter", hint.Label.String!);
    }

    [Fact]
    public async Task LocKeyTag_AbsentFromIndex_ProducesMissingHint()
    {
        const string xml = "<GameObjectType>\n  <Text_ID>TEXT_MISSING</Text_ID>\n</GameObjectType>";
        var schema = new LocKeySchemaProvider("Text_ID");
        var loc = new ValueLocalisationIndex("TEXT_OTHER", "Something else");

        var (handler, _) = Build(xml, schema, loc);
        var result = await handler.Handle(Params(), CancellationToken.None);

        var hint = Assert.Single(result!);
        Assert.Contains("MISSING", hint.Label.String!);
    }

    [Fact]
    public async Task NonLocKeyTag_ProducesNoHint()
    {
        const string xml = "<GameObjectType>\n  <Name>X_Wing</Name>\n</GameObjectType>";
        // schema returns None referenceKind for Name
        var (handler, _) = Build(xml);
        var result = await handler.Handle(Params(), CancellationToken.None);

        Assert.Empty(result!);
    }

    [Fact]
    public async Task MultipleLocKeyTags_ProducesHintForEach()
    {
        const string xml =
            "<GameObjectType>\n" +
            "  <Text_ID>TEXT_NAME</Text_ID>\n" +
            "  <Tooltip>TEXT_TIP</Tooltip>\n" +
            "</GameObjectType>";
        var schema = new LocKeySchemaProvider("Text_ID", "Tooltip");
        var loc = new ValueLocalisationIndex(
            ("TEXT_NAME", "X-Wing Fighter"),
            ("TEXT_TIP", "A fast rebel craft"));

        var (handler, _) = Build(xml, schema, loc);
        var result = await handler.Handle(Params(), CancellationToken.None);

        Assert.Equal(2, result!.Count());
    }

    [Fact]
    public async Task EmptyTagValue_ProducesNoHint()
    {
        const string xml = "<GameObjectType>\n  <Text_ID></Text_ID>\n</GameObjectType>";
        var schema = new LocKeySchemaProvider("Text_ID");
        var loc = new ValueLocalisationIndex();

        var (handler, _) = Build(xml, schema, loc);
        var result = await handler.Handle(Params(), CancellationToken.None);

        Assert.Empty(result!);
    }

    // ── range filtering ──────────────────────────────────────────────────────

    [Fact]
    public async Task TagOutsideRange_ProducesNoHint()
    {
        const string xml =
            "<GameObjectType>\n" +
            "  <Text_ID>TEXT_NAME</Text_ID>\n" + // line 1
            "</GameObjectType>";
        var schema = new LocKeySchemaProvider("Text_ID");
        var loc = new ValueLocalisationIndex("TEXT_NAME", "X-Wing Fighter");
        // Request range covers only line 0
        var range = new LspRange(new Position(0, 0), new Position(0, int.MaxValue));

        var (handler, _) = Build(xml, schema, loc);
        var result = await handler.Handle(Params(range), CancellationToken.None);

        Assert.Empty(result!);
    }

    // ── non-EaW file ─────────────────────────────────────────────────────────

    [Fact]
    public async Task NonEaWFile_ReturnsNull()
    {
        const string xml = "<GameObjectType><Text_ID>TEXT_NAME</Text_ID></GameObjectType>";
        var host = new FakeWorkspaceHost();
        host.Set(Uri, xml);
        var index = new FakeIndexService();
        var handler = new XmlInlayHintHandler(
            TestParseCache.For(host), index, new EmptySchemaProvider(),
            new DenyAllEaWContext(),
            new FileHelper(new MockFileSystem()),
            NullLogger<XmlInlayHintHandler>.Instance,
            new XmlInlayHintRegistry([]));

        var result = await handler.Handle(Params(), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_SameContentTwice_ReusesOneParse()
    {
        // Inlay hints are re-requested by clients on every scroll — with unchanged content the
        // second request must be a cache hit, not a re-parse.
        const string xml = "<GameObjectType><Text_ID>TEXT_NAME</Text_ID></GameObjectType>";
        var host = new FakeWorkspaceHost();
        host.Set(Uri, xml);
        var cache = TestParseCache.For(host);
        var handler = new XmlInlayHintHandler(
            cache, new FakeIndexService(), new EmptySchemaProvider(),
            new AllowAllEaWContext(),
            new FileHelper(new MockFileSystem()),
            NullLogger<XmlInlayHintHandler>.Instance,
            new XmlInlayHintRegistry([]));

        _ = await handler.Handle(Params(), CancellationToken.None);
        _ = await handler.Handle(Params(), CancellationToken.None);

        var (hits, misses, _) = cache.Statistics;
        Assert.Equal(1, misses);
        Assert.Equal(1, hits);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

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

        public void Set(string uri, string text)
        {
            _docs[uri] = new TrackedDocument(uri, text, 0);
        }
    }

    private sealed class FakeIndexService : IGameIndexService
    {
        private ILocalisationIndex _loc = new EmptyStubLoc();

        public ILocalisationIndex Localisation
        {
            set => Current = Current with { Localisation = value };
        }

        public GameIndex Current { get; private set; } = GameIndex.Empty;

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

        public void ApplyModelBones(ImmutableDictionary<string, ImmutableArray<string>> bones)
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
            return NullDisp.Instance;
        }

        private sealed class NullDisp : IDisposable
        {
            public static readonly NullDisp Instance = new();

            public void Dispose()
            {
            }
        }

        private sealed class EmptyStubLoc : ILocalisationIndex
        {
            public bool ContainsKey(string key)
            {
                return false;
            }

            public IEnumerable<string> Keys => [];

            public string? GetValue(string key)
            {
                return null;
            }
        }
    }
}

// ── file-scoped helpers ──────────────────────────────────────────────────────

file sealed class LocKeySchemaProvider : ISchemaProvider
{
    private readonly HashSet<string> _locKeyTags;

    public LocKeySchemaProvider(params string[] locKeyTagNames)
    {
        _locKeyTags = new HashSet<string>(locKeyTagNames, StringComparer.OrdinalIgnoreCase);
    }

    public XmlTagDefinition? GetTag(string name)
    {
        if (!_locKeyTags.Contains(name)) return null;
        return new XmlTagDefinition
        {
            Tag = name,
            ValueType = XmlValueType.NameReference,
            ReferenceKind = ReferenceKind.LocalisationKey
        };
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string _)
    {
        return null;
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => [];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}

file sealed class ValueLocalisationIndex : ILocalisationIndex
{
    private readonly Dictionary<string, string> _values;

    public ValueLocalisationIndex(params (string key, string value)[] pairs)
    {
        _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs) _values[k] = v;
    }

    public ValueLocalisationIndex(string key, string value)
        : this((key, value))
    {
    }

    public ValueLocalisationIndex() : this([])
    {
    }

    public bool ContainsKey(string key)
    {
        return _values.ContainsKey(key);
    }

    public IEnumerable<string> Keys => _values.Keys;

    public string? GetValue(string key)
    {
        return _values.TryGetValue(key, out var v) ? v : null;
    }
}
