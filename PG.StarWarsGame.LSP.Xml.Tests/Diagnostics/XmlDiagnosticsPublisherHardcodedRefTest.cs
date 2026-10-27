// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests.Diagnostics;

public sealed class XmlDiagnosticsPublisherHardcodedRefTest
{
    private static XmlDiagnosticsPublisher BuildPublisher(ISchemaProvider schema)
    {
        return XmlPublisherFixture.Create(
            new RecordingDiagnosticsSink(),
            new FakeGameIndexService(),
            new FakeGameWorkspaceHost(),
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

    private static HardcodedReferenceSetValue DeprecatedValue(string name, params string[] groups)
    {
        return new HardcodedReferenceSetValue
        {
            Name = name, Groups = groups,
            Notes = [new SchemaNote(SchemaNoteKind.Deprecated, new Dictionary<string, string>())]
        };
    }

    private static XmlTagDefinition BehaviorTag(string tagName, params string[] valueGroups)
    {
        return new XmlTagDefinition
        {
            Tag = tagName,
            ValueType = XmlValueType.TypeReferenceList,
            ReferenceKind = ReferenceKind.HardcodedSet,
            ValueGroups = valueGroups
        };
    }

    // ── basic validation ─────────────────────────────────────────────────────

    [Fact]
    public void Unknown_token_produces_error_diagnostic()
    {
        var schema = new StubHardcodedSchemaProvider(
            BehaviorTag("Behavior"),
            BehaviorModuleSet(Value("GenericTransport"))).Build();
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
            BehaviorModuleSet(Value("GenericTransport"), Value("SlaveCraftBehavior"))).Build();
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
            []).Build();
        var publisher = BuildPublisher(schema);

        const string xml =
            "<GameObjectFiles><GameObject><Behavior>SOME_UNKNOWN_MODULE</Behavior></GameObject></GameObjectFiles>";

        var diags = publisher.CollectHardcodedRefDiagnostics("file:///units.xml", xml, GameIndex.Empty);

        Assert.Empty(diags);
    }

    [Fact]
    public void Deprecated_token_produces_warning_diagnostic()
    {
        var schema = new StubHardcodedSchemaProvider(
            BehaviorTag("Behavior"),
            BehaviorModuleSet(Value("GenericTransport"), DeprecatedValue("OLD_MODULE"))).Build();
        var publisher = BuildPublisher(schema);

        const string xml =
            "<GameObjectFiles><GameObject><Behavior>OLD_MODULE</Behavior></GameObject></GameObjectFiles>";

        var diags = publisher.CollectHardcodedRefDiagnostics("file:///units.xml", xml, GameIndex.Empty);

        var diag = Assert.Single(diags);
        Assert.Contains("OLD_MODULE", diag.Message);
        Assert.Contains("deprecated", diag.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
    }

    [Fact]
    public void Deprecated_token_alongside_valid_token_produces_only_warning()
    {
        var schema = new StubHardcodedSchemaProvider(
            BehaviorTag("Behavior"),
            BehaviorModuleSet(Value("GenericTransport"), DeprecatedValue("OLD_MODULE"))).Build();
        var publisher = BuildPublisher(schema);

        const string xml =
            "<GameObjectFiles><GameObject><Behavior>GenericTransport, OLD_MODULE</Behavior></GameObject></GameObjectFiles>";

        var diags = publisher.CollectHardcodedRefDiagnostics("file:///units.xml", xml, GameIndex.Empty);

        var diag = Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Warning, diag.Severity);
    }

    // ── group filtering ──────────────────────────────────────────────────────

    [Fact]
    public void ValueGroup_on_tag_filters_valid_tokens_to_matching_group()
    {
        var schema = new StubHardcodedSchemaProvider(
            BehaviorTag("SpaceBehavior", "space"),
            BehaviorModuleSet(Value("SpaceFighter", "space"), Value("InfantryUnit", "land"))).Build();
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
            BehaviorModuleSet(Value("GenericTransport" /* no groups - valid everywhere */))).Build();
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
            BehaviorModuleSet(Value("SpaceFighter", "space"), Value("InfantryUnit", "land"))).Build();
        var publisher = BuildPublisher(schema);

        const string xml =
            "<GameObjectFiles><GameObject><SpaceBehavior>SpaceFighter</SpaceBehavior></GameObject></GameObjectFiles>";

        var diags = publisher.CollectHardcodedRefDiagnostics("file:///units.xml", xml, GameIndex.Empty);

        Assert.Empty(diags);
    }

    // ── multi-group filtering ────────────────────────────────────────────────

    [Fact]
    public void MultiValueGroup_token_matching_any_group_is_valid()
    {
        var schema = new StubHardcodedSchemaProvider(
            BehaviorTag("Behavior", "land", "space"),
            BehaviorModuleSet(Value("LandUnit", "land"), Value("SpaceUnit", "space"))).Build();
        var publisher = BuildPublisher(schema);

        const string xml =
            "<GameObjectFiles><GameObject><Behavior>LandUnit SpaceUnit</Behavior></GameObject></GameObjectFiles>";

        var diags = publisher.CollectHardcodedRefDiagnostics("file:///units.xml", xml, GameIndex.Empty);

        Assert.Empty(diags);
    }

    [Fact]
    public void MultiValueGroup_token_matching_no_group_is_invalid()
    {
        var schema = new StubHardcodedSchemaProvider(
            BehaviorTag("LandBehavior", "land", "space"),
            BehaviorModuleSet(Value("AirUnit", "air"), Value("LandUnit", "land"))).Build();
        var publisher = BuildPublisher(schema);

        const string xml =
            "<GameObjectFiles><GameObject><LandBehavior>AirUnit</LandBehavior></GameObject></GameObjectFiles>";

        var diags = publisher.CollectHardcodedRefDiagnostics("file:///units.xml", xml, GameIndex.Empty);

        var diag = Assert.Single(diags);
        Assert.Contains("AirUnit", diag.Message);
    }

    // ── file-type fast-path ──────────────────────────────────────────────────

    [Fact]
    public void CollectHardcodedRefDiagnostics_SkipsWalk_WhenFileTypeHasNoHardcodedRefTags()
    {
        // File type "CommandBarComponent" has no hardcoded-ref tags (GetTagsForType returns []).
        // Even though there is a global "Behavior" hardcoded tag, the walk should be skipped
        // for this file type, so no diagnostic is emitted for an invalid value.
        var schema = new StubHardcodedSchemaProvider(
            BehaviorTag("Behavior"),
            BehaviorModuleSet(Value("GenericTransport"))).Build();
        var registry = new StubFileTypeRegistryWithTypes("file:///units.xml", ["CommandBarComponent"]);
        var publisher = BuildPublisher(schema, registry);

        const string xml =
            "<CommandBarComponents><CommandBarComponent><Behavior>TYPO_MODULE</Behavior></CommandBarComponent></CommandBarComponents>";

        var diags = publisher.CollectHardcodedRefDiagnostics("file:///units.xml", xml, GameIndex.Empty);

        Assert.Empty(diags);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private static XmlDiagnosticsPublisher BuildPublisher(ISchemaProvider schema, IFileTypeRegistry registry)
    {
        return XmlPublisherFixture.Create(
            new RecordingDiagnosticsSink(),
            new FakeGameIndexService(),
            new FakeGameWorkspaceHost(),
            schema,
            new XmlDiagnosticsHandlerRegistry([]),
            new StubDocumentFactProducer(),
            new StubIndexFactProducer(),
            new StubStoryFactProducer(),
            NullLogger<XmlDiagnosticsPublisher>.Instance,
            registry,
            new FileHelper(new MockFileSystem()));
    }
}

file sealed class StubHardcodedSchemaProvider
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

    public FakeSchemaProvider Build()
    {
        var builder = new SchemaProviderBuilder();
        foreach (var item in _hardcodedSets)
            builder.WithHardcodedSet(item);
        foreach (var item in _tags.Values)
            builder.WithTag(item);
        return builder.Build();
    }
}

file sealed class StubDocumentFactProducer : IXmlDocumentFactProducer
{
    public IReadOnlyList<XmlFact> Produce(ParsedXmlDocument document, string documentUri)
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
    public IReadOnlyList<XmlFact> Produce(ParsedXmlDocument document, string documentUri)
    {
        return [];
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

file sealed class StubFileTypeRegistryWithTypes : IFileTypeRegistry
{
    private readonly Dictionary<string, ImmutableArray<string>> _types;

    public StubFileTypeRegistryWithTypes(string uri, string[] types)
    {
        _types = new Dictionary<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [uri] = types.ToImmutableArray()
        };
    }

    public ImmutableArray<string> GetTypesForFile(string normalizedPath)
    {
        return _types.TryGetValue(normalizedPath, out var types) ? types : ImmutableArray<string>.Empty;
    }

    public void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string normalizedPath)
    {
    }

    public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
        new Dictionary<string, ImmutableArray<string>>(_types);
}