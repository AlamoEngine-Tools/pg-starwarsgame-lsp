// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Diagnostics;

public sealed class XmlDiagnosticsPublisherHardcodedRefTest
{
    private static XmlDiagnosticsPublisher BuildPublisher(ISchemaProvider schema)
    {
        return new XmlDiagnosticsPublisher(
            _ => { },
            new StubIndexService(),
            new StubWorkspaceHost(),
            schema,
            new XmlDiagnosticsHandlerRegistry([]),
            new StubDocumentFactProducer(),
            new StubIndexFactProducer(),
            new StubStoryFactProducer(),
            NullLogger<XmlDiagnosticsPublisher>.Instance,
            new StubFileTypeRegistry(),
            new FileHelper(new MockFileSystem()));
    }

    private static HardcodedReferenceSet BehaviorModuleSet(params HardcodedReferenceSetValue[] values)
    {
        return new HardcodedReferenceSet
        {
            Name = "BehaviorModule",
            Values = values
        };
    }

    private static HardcodedReferenceSetValue Value(string name, params string[] groups)
    {
        return new HardcodedReferenceSetValue { Name = name, Groups = groups };
    }

    private static XmlTagDefinition BehaviorTag(string tagName, string? valueGroup = null)
    {
        return new XmlTagDefinition
        {
            Tag = tagName,
            ValueType = XmlValueType.TypeReferenceList,
            ReferenceKind = ReferenceKind.HardcodedSet,
            ValueGroup = valueGroup
        };
    }

    // ── basic validation ─────────────────────────────────────────────────────

    [Fact]
    public void Unknown_token_produces_error_diagnostic()
    {
        var schema = new StubHardcodedSchemaProvider(
            BehaviorTag("Behavior"),
            BehaviorModuleSet(Value("GenericTransport")));
        var publisher = BuildPublisher(schema);

        const string xml =
            "<GameObjectFiles><GameObject><Behavior>GenericTransport, TYPO_MODULE</Behavior></GameObject></GameObjectFiles>";

        var diags = publisher.CollectHardcodedRefDiagnostics("file:///units.xml", xml, GameIndex.Empty);

        var diag = Assert.Single(diags);
        Assert.Contains("TYPO_MODULE", diag.Message);
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
    }

    [Fact]
    public void All_known_tokens_produce_no_diagnostic()
    {
        var schema = new StubHardcodedSchemaProvider(
            BehaviorTag("Behavior"),
            BehaviorModuleSet(Value("GenericTransport"), Value("SlaveCraftBehavior")));
        var publisher = BuildPublisher(schema);

        const string xml =
            "<GameObjectFiles><GameObject><Behavior>GenericTransport SlaveCraftBehavior</Behavior></GameObject></GameObjectFiles>";

        var diags = publisher.CollectHardcodedRefDiagnostics("file:///units.xml", xml, GameIndex.Empty);

        Assert.Empty(diags);
    }

    [Fact]
    public void Absent_schema_set_produces_no_diagnostic()
    {
        // Tag references "BehaviorModule" but schema has no such set
        var schema = new StubHardcodedSchemaProvider(
            BehaviorTag("Behavior"),
            []);
        var publisher = BuildPublisher(schema);

        const string xml =
            "<GameObjectFiles><GameObject><Behavior>SOME_UNKNOWN_MODULE</Behavior></GameObject></GameObjectFiles>";

        var diags = publisher.CollectHardcodedRefDiagnostics("file:///units.xml", xml, GameIndex.Empty);

        Assert.Empty(diags);
    }

    // ── group filtering ──────────────────────────────────────────────────────

    [Fact]
    public void ValueGroup_on_tag_filters_valid_tokens_to_matching_group()
    {
        var schema = new StubHardcodedSchemaProvider(
            BehaviorTag("SpaceBehavior", "space"),
            BehaviorModuleSet(Value("SpaceFighter", "space"), Value("InfantryUnit", "land")));
        var publisher = BuildPublisher(schema);

        const string xml =
            "<GameObjectFiles><GameObject><SpaceBehavior>InfantryUnit</SpaceBehavior></GameObject></GameObjectFiles>";

        var diags = publisher.CollectHardcodedRefDiagnostics("file:///units.xml", xml, GameIndex.Empty);

        var diag = Assert.Single(diags);
        Assert.Contains("InfantryUnit", diag.Message);
    }

    [Fact]
    public void Token_with_empty_groups_is_valid_for_any_ValueGroup()
    {
        var schema = new StubHardcodedSchemaProvider(
            BehaviorTag("SpaceBehavior", "space"),
            BehaviorModuleSet(Value("GenericTransport" /* no groups — valid everywhere */)));
        var publisher = BuildPublisher(schema);

        const string xml =
            "<GameObjectFiles><GameObject><SpaceBehavior>GenericTransport</SpaceBehavior></GameObject></GameObjectFiles>";

        var diags = publisher.CollectHardcodedRefDiagnostics("file:///units.xml", xml, GameIndex.Empty);

        Assert.Empty(diags);
    }

    [Fact]
    public void Matching_group_token_is_valid()
    {
        var schema = new StubHardcodedSchemaProvider(
            BehaviorTag("SpaceBehavior", "space"),
            BehaviorModuleSet(Value("SpaceFighter", "space"), Value("InfantryUnit", "land")));
        var publisher = BuildPublisher(schema);

        const string xml =
            "<GameObjectFiles><GameObject><SpaceBehavior>SpaceFighter</SpaceBehavior></GameObject></GameObjectFiles>";

        var diags = publisher.CollectHardcodedRefDiagnostics("file:///units.xml", xml, GameIndex.Empty);

        Assert.Empty(diags);
    }

    // ── fakes ────────────────────────────────────────────────────────────────
}

file sealed class StubHardcodedSchemaProvider : ISchemaProvider
{
    private readonly List<HardcodedReferenceSet> _hardcodedSets;
    private readonly Dictionary<string, XmlTagDefinition> _tags;

    public StubHardcodedSchemaProvider(
        XmlTagDefinition tag,
        HardcodedReferenceSet set)
        : this(tag, [set])
    {
    }

    public StubHardcodedSchemaProvider(
        XmlTagDefinition tag,
        List<HardcodedReferenceSet> hardcodedSets)
    {
        _hardcodedSets = hardcodedSets;
        var wiredTag = tag.ReferenceKind == ReferenceKind.HardcodedSet && hardcodedSets.Count > 0
            ? tag with { HardcodedSet = hardcodedSets[0] }
            : tag;
        _tags = new Dictionary<string, XmlTagDefinition>(StringComparer.OrdinalIgnoreCase)
            { [wiredTag.Tag] = wiredTag };
    }

    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => _hardcodedSets;
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<XmlTagDefinition> AllTags => [.. _tags.Values];
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public XmlTagDefinition? GetTag(string name)
    {
        return _tags.GetValueOrDefault(name);
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return [];
    }

    public GameObjectTypeDefinition? GetObjectType(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
    {
        return [];
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }
}

file sealed class StubDocumentFactProducer : IXmlDocumentFactProducer
{
    public IReadOnlyList<XmlFact> Produce(string xmlText, string documentUri)
    {
        return [];
    }
}

file sealed class StubIndexFactProducer : IXmlIndexFactProducer
{
    public IReadOnlyList<XmlFact> Produce(string documentUri, GameIndex index)
    {
        return [];
    }
}

file sealed class StubStoryFactProducer : IStoryFactProducer
{
    public IReadOnlyList<XmlFact> Produce(string xmlText, string documentUri)
    {
        return [];
    }
}

file sealed class StubIndexService : IGameIndexService
{
    public GameIndex Current => GameIndex.Empty;
    public event Action<GameIndex>? IndexChanged;

    public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public void RemoveDocument(string uri)
    {
    }

    public void ApplyBaseline(BaselineIndex baseline)
    {
    }

    public IDisposable BeginBulkUpdate()
    {
        return NullDisposable.Instance;
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}

file sealed class StubFileTypeRegistry : IFileTypeRegistry
{
    public ImmutableArray<string> GetTypesForFile(string normalizedPath)
    {
        return ImmutableArray<string>.Empty;
    }

    public void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string normalizedPath)
    {
    }

    public IReadOnlyDictionary<string, ImmutableArray<string>> All => new Dictionary<string, ImmutableArray<string>>();
}

file sealed class StubWorkspaceHost : IGameWorkspaceHost
{
    private readonly Dictionary<string, TrackedDocument> _docs = [];

    public void AddOrUpdate(string uri, string text, int version)
    {
        _docs[uri] = new TrackedDocument(uri, text, version);
    }

    public void Remove(string uri)
    {
        _docs.Remove(uri);
    }

    public bool TryGet(string uri, out TrackedDocument doc)
    {
        if (_docs.TryGetValue(uri, out var d))
        {
            doc = d;
            return true;
        }

        doc = default!;
        return false;
    }

    public IEnumerable<TrackedDocument> All => _docs.Values;
}