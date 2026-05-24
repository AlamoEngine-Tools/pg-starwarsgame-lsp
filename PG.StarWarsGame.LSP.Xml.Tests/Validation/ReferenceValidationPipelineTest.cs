// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Parsing;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation;

/// <summary>
/// Pipeline integration tests: parser → XmlIndexFactProducer → diagnostic handlers.
/// These guard against regressions where a correct reference produces a false-positive diagnostic.
/// </summary>
public sealed class ReferenceValidationPipelineTest
{
    private static readonly DiagnosticsContext EmptyCtx = new(
        new Handlers.EmptySchemaProvider(), GameIndex.Empty, "file:///test.xml", "en");

    // ── helpers ──────────────────────────────────────────────────────────────

    private static XmlGameDocumentParser BuildParser(PipelineFakeSchema schema)
    {
        return new XmlGameDocumentParser(
            new FileHelper(new MockFileSystem()),
            schema,
            new PipelineFakeFileTypeRegistry(),
            NullLogger<XmlGameDocumentParser>.Instance);
    }

    private static GameIndex BuildIndex(DocumentIndex doc, params (string id, string typeName)[] symbols)
    {
        var defs = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty;
        foreach (var (id, typeName) in symbols)
            defs = defs.Add(id, [new GameSymbol(id, GameSymbolKind.XmlObject, typeName, new UnknownOrigin(""), null)]);

        return GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty.Add("file:///test.xml", doc),
            WorkspaceDefinitions = defs
        };
    }

    private static IReadOnlyList<XmlDiagnosticResult> TypeMismatchDiagnostics(
        IReadOnlyList<XmlFact> facts)
    {
        var handler = new TypeMismatchHandler();
        return facts
            .OfType<XmlReferenceFact>()
            .SelectMany(f => handler.Handle(f, EmptyCtx))
            .ToList();
    }

    // ── GameObjectType wildcard — no false-positive ───────────────────────────

    [Fact]
    public async Task GameObjectType_ref_with_SpaceUnit_symbol_emits_no_TypeMismatch()
    {
        var schema = new PipelineFakeSchema();
        schema.AddTag(new XmlTagDefinition
        {
            Tag = "Squadron_Units",
            ValueType = XmlValueType.GameObjectTypeReferenceList,
            ReferenceKind = ReferenceKind.XmlObject,
            ObjectType = new GameObjectTypeDefinition { TypeName = "GameObjectType" }
        });

        const string xml = """
            <GameObjectFiles>
                <GameObjectType Name="TEST_SQUADRON">
                    <Squadron_Units>FIGHTER_A FIGHTER_B</Squadron_Units>
                </GameObjectType>
            </GameObjectFiles>
            """;

        var docIndex = await BuildParser(schema).ParseAsync("file:///test.xml", xml, 1, CancellationToken.None);
        Assert.Equal(2, docIndex.References.Length);

        var index = BuildIndex(docIndex, ("FIGHTER_A", "SpaceUnit"), ("FIGHTER_B", "SpaceUnit"));
        var facts = new XmlIndexFactProducer().Produce("file:///test.xml", index);
        var diagnostics = TypeMismatchDiagnostics(facts);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task GameObjectType_ref_with_mixed_types_emits_no_TypeMismatch()
    {
        var schema = new PipelineFakeSchema();
        schema.AddTag(new XmlTagDefinition
        {
            Tag = "Encyclopedia_Good_Against",
            ValueType = XmlValueType.TypeReferenceList,
            ReferenceKind = ReferenceKind.XmlObject,
            ObjectType = new GameObjectTypeDefinition { TypeName = "GameObjectType" }
        });

        const string xml = """
            <GameObjectFiles>
                <GameObjectType Name="AT_AT">
                    <Encyclopedia_Good_Against>REBEL_SOLDIER X_WING</Encyclopedia_Good_Against>
                </GameObjectType>
            </GameObjectFiles>
            """;

        var docIndex = await BuildParser(schema).ParseAsync("file:///test.xml", xml, 1, CancellationToken.None);
        Assert.Equal(2, docIndex.References.Length);

        var index = BuildIndex(docIndex, ("REBEL_SOLDIER", "GroundCompanyUnit"), ("X_WING", "SpaceUnit"));
        var facts = new XmlIndexFactProducer().Produce("file:///test.xml", index);
        var diagnostics = TypeMismatchDiagnostics(facts);

        Assert.Empty(diagnostics);
    }

    // ── Real type mismatch (non-GameObjectType) still fires ───────────────────

    [Fact]
    public async Task Non_GameObjectType_ref_with_wrong_symbol_type_emits_TypeMismatch()
    {
        var schema = new PipelineFakeSchema();
        schema.AddTag(new XmlTagDefinition
        {
            Tag = "Faction",
            ValueType = XmlValueType.FactionReference,
            ReferenceKind = ReferenceKind.XmlObject,
            ObjectType = new GameObjectTypeDefinition { TypeName = "Faction" }
        });

        const string xml = """
            <GameObjectFiles>
                <GameObjectType Name="SOME_UNIT">
                    <Faction>REBEL_FIGHTER_01</Faction>
                </GameObjectType>
            </GameObjectFiles>
            """;

        var docIndex = await BuildParser(schema).ParseAsync("file:///test.xml", xml, 1, CancellationToken.None);
        Assert.Single(docIndex.References);

        var index = BuildIndex(docIndex, ("REBEL_FIGHTER_01", "SpaceUnit"));
        var facts = new XmlIndexFactProducer().Produce("file:///test.xml", index);
        var diagnostics = TypeMismatchDiagnostics(facts);

        var d = Assert.Single(diagnostics);
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("REBEL_FIGHTER_01", d.Message);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class PipelineFakeSchema : ISchemaProvider
    {
        private readonly Dictionary<string, XmlTagDefinition> _tags =
            new(StringComparer.OrdinalIgnoreCase);

        public void AddTag(XmlTagDefinition tag) => _tags[tag.Tag] = tag;

        public XmlTagDefinition? GetTag(string name) => _tags.GetValueOrDefault(name);
        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _) => [];
        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _) => [];
        public IReadOnlyList<XmlTagDefinition> AllTags => [.. _tags.Values];
        public GameObjectTypeDefinition? GetObjectType(string _) => null;
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public EnumDefinition? GetEnum(string _) => null;
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];
        public event EventHandler? SchemaRefreshed { add { } remove { } }
    }

    private sealed class PipelineFakeFileTypeRegistry : IFileTypeRegistry
    {
        public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
            ImmutableDictionary<string, ImmutableArray<string>>.Empty;
        public ImmutableArray<string> GetTypesForFile(string _) => ImmutableArray<string>.Empty;
        public void RegisterFile(string _, ImmutableArray<string> __) { }
        public void UnregisterFile(string _) { }
    }
}
