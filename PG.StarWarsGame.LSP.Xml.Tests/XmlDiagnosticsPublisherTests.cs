using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Validation;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlDiagnosticsPublisherTests
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static XmlTagDefinition MakeTag(string name, bool multipleAllowed = false)
    {
        return new XmlTagDefinition { Tag = name, ValueType = XmlValueType.Float, MultipleAllowed = multipleAllowed };
    }

    private static GameObjectTypeDefinition MakeType(string name)
    {
        return new GameObjectTypeDefinition { TypeName = name };
    }

    // CollectDiagnostics-only tests still use a no-op publish action.
    private static XmlDiagnosticsPublisher BuildPublisher(FakeSchemaProvider schema)
    {
        return new XmlDiagnosticsPublisher(
            _ => { },
            new FakeGameIndexService(),
            new FakeGameWorkspaceHost(),
            schema,
            new FakeValidatorRegistry(),
            NullLogger<XmlDiagnosticsPublisher>.Instance);
    }

    // Subscription / routing tests use this builder so they can control the index service.
    private static (XmlDiagnosticsPublisher publisher,
                    List<PublishDiagnosticsParams> published,
                    FakeGameIndexService indexService,
                    FakeGameWorkspaceHost workspaceHost) BuildSubscribed(FakeSchemaProvider? schema = null)
    {
        var published     = new List<PublishDiagnosticsParams>();
        var indexService  = new FakeGameIndexService();
        var workspaceHost = new FakeGameWorkspaceHost();
        var publisher = new XmlDiagnosticsPublisher(
            p => published.Add(p),
            indexService,
            workspaceHost,
            schema ?? new FakeSchemaProvider(),
            new FakeValidatorRegistry(),
            NullLogger<XmlDiagnosticsPublisher>.Instance);
        return (publisher, published, indexService, workspaceHost);
    }

    // Builds a GameIndex containing exactly one empty document.
    private static GameIndex IndexWithDoc(string uri, int version = 1)
    {
        var doc = new DocumentIndex(uri, version,
            ImmutableArray<GameSymbol>.Empty, ImmutableArray<GameReference>.Empty);
        return new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(uri, doc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    // Builds a GameIndex where two files both declare the same ID.
    private static GameIndex IndexWithDuplicateId(string id, string uri1, string uri2)
    {
        var sym1 = new GameSymbol(id, GameSymbolKind.XmlObject, "Unit", new FileOrigin(uri1, 0, null), null);
        var sym2 = new GameSymbol(id, GameSymbolKind.XmlObject, "Unit", new FileOrigin(uri2, 0, null), null);
        var doc1 = new DocumentIndex(uri1, 1, [sym1], []);
        var doc2 = new DocumentIndex(uri2, 1, [sym2], []);
        return new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(uri1, doc1).Add(uri2, doc2),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add(id, ImmutableArray.Create(sym1, sym2)),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);
    }

    // Builds a GameIndex with one document that has a single reference to targetId.
    private static GameIndex IndexWithRef(string documentUri, string targetId, string? targetType = null)
    {
        var reference = new GameReference(targetId, GameSymbolKind.XmlObject, targetType,
            documentUri, 2, 0, targetId.Length);
        var doc = new DocumentIndex(documentUri, 1, [], [reference]);
        return new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add(documentUri, doc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add(targetId, ImmutableArray.Create(reference)));
    }

    // ── duplicate detection (per-tag) ────────────────────────────────────────

    [Fact]
    public void CollectDiagnostics_SingleOccurrence_NoDiagnostics()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(MakeTag("Max_Speed"));
        var publisher = BuildPublisher(schema);

        var diags = publisher.CollectDiagnostics("""
                                                 <SpaceUnit>
                                                   <Max_Speed>500</Max_Speed>
                                                 </SpaceUnit>
                                                 """);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectDiagnostics_TwoDuplicateSingletons_BothFlaggedWithCrossReferences()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(MakeTag("Max_Speed"));
        var publisher = BuildPublisher(schema);

        // Lines 2 and 3 (1-based) in the raw string below
        var diags = publisher.CollectDiagnostics("""
                                                 <SpaceUnit>
                                                   <Max_Speed>500</Max_Speed>
                                                   <Max_Speed>600</Max_Speed>
                                                 </SpaceUnit>
                                                 """);

        Assert.Equal(2, diags.Count);
        Assert.All(diags, d => Assert.Contains("Max_Speed", d.Message));
        Assert.All(diags, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        // Each diagnostic must mention the other occurrence's line
        Assert.Contains("3", diags[0].Message); // first occurrence references line 3
        Assert.Contains("2", diags[1].Message); // second occurrence references line 2
    }

    [Fact]
    public void CollectDiagnostics_ThreeDuplicateSingletons_EachReferencesOtherTwo()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(MakeTag("Max_Speed"));
        var publisher = BuildPublisher(schema);

        var diags = publisher.CollectDiagnostics("""
                                                 <SpaceUnit>
                                                   <Max_Speed>100</Max_Speed>
                                                   <Max_Speed>200</Max_Speed>
                                                   <Max_Speed>300</Max_Speed>
                                                 </SpaceUnit>
                                                 """);

        Assert.Equal(3, diags.Count);
        // First occurrence: "Also at lines 3, 4."
        Assert.Contains("3", diags[0].Message);
        Assert.Contains("4", diags[0].Message);
    }

    [Fact]
    public void CollectDiagnostics_MultipleAllowedTag_NoDiagnostics()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(MakeTag("SFXEvent_Attack_Hardpoint", true));
        var publisher = BuildPublisher(schema);

        var diags = publisher.CollectDiagnostics("""
                                                 <HardPoint>
                                                   <SFXEvent_Attack_Hardpoint>Sfx_A</SFXEvent_Attack_Hardpoint>
                                                   <SFXEvent_Attack_Hardpoint>Sfx_B</SFXEvent_Attack_Hardpoint>
                                                 </HardPoint>
                                                 """);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectDiagnostics_SameSingletonInTwoDifferentObjects_NoDiagnostics()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(MakeTag("Max_Speed"));
        schema.AddType(MakeType("SpaceUnit"));
        var publisher = BuildPublisher(schema);

        // Two separate SpaceUnit objects, each with one Max_Speed — not duplicates
        var diags = publisher.CollectDiagnostics("""
                                                 <GameObjectFiles>
                                                   <SpaceUnit Name="UnitA">
                                                     <Max_Speed>500</Max_Speed>
                                                   </SpaceUnit>
                                                   <SpaceUnit Name="UnitB">
                                                     <Max_Speed>300</Max_Speed>
                                                   </SpaceUnit>
                                                 </GameObjectFiles>
                                                 """);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectDiagnostics_NonTypeRootMatchingTagName_NotValidatedAsTag()
    {
        var schema = new FakeSchemaProvider();
        // Hardpoints is a singleton tag in the schema but NOT a registered type
        schema.AddTag(MakeTag("Hardpoints"));
        var publisher = BuildPublisher(schema);

        // Root <Hardpoints> must not be validated as a singleton tag field
        var diags = publisher.CollectDiagnostics("""
                                                 <Hardpoints>
                                                   <Hardpoint></Hardpoint>
                                                 </Hardpoints>
                                                 """);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectDiagnostics_RootTypeCollidesWithTagName_NoFalsePositiveDuplicate()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(MakeTag("Faction")); // Faction is also a singleton tag elsewhere
        schema.AddType(MakeType("Faction")); // but here it is a type container
        var publisher = BuildPublisher(schema);

        // Two Faction instances at root level — must not be flagged as duplicate singleton tags
        var diags = publisher.CollectDiagnostics("""
                                                 <Faction Name="EMPIRE">
                                                   <Rank>1</Rank>
                                                 </Faction>
                                                 <Faction Name="REBEL">
                                                   <Rank>2</Rank>
                                                 </Faction>
                                                 """);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectDiagnostics_DuplicateUnknownTag_NoDiagnostics()
    {
        var schema = new FakeSchemaProvider();
        // Tag not registered → unknown → no duplicate error
        var publisher = BuildPublisher(schema);

        var diags = publisher.CollectDiagnostics("""
                                                 <SpaceUnit>
                                                   <Unknown_Tag>1</Unknown_Tag>
                                                   <Unknown_Tag>2</Unknown_Tag>
                                                 </SpaceUnit>
                                                 """);

        Assert.Empty(diags);
    }

    // ── subscription / routing ───────────────────────────────────────────────

    [Fact]
    public void IndexChanged_Fires_Publishes_For_Each_Document_In_Index()
    {
        var (_, published, indexService, _) = BuildSubscribed();

        indexService.Fire(IndexWithDoc("file:///a.xml"));

        Assert.Single(published);
        Assert.Equal("file:///a.xml", published[0].Uri.ToString());
    }

    [Fact]
    public void IndexChanged_TwoDocuments_PublishesTwice()
    {
        var (_, published, indexService, _) = BuildSubscribed();

        var index = IndexWithDoc("file:///a.xml") with
        {
            Documents = IndexWithDoc("file:///a.xml").Documents
                .Add("file:///b.xml", new DocumentIndex("file:///b.xml", 1, [], []))
        };
        indexService.Fire(index);

        Assert.Equal(2, published.Count);
    }

    [Fact]
    public void IndexChanged_Empty_Index_Publishes_Nothing()
    {
        var (_, published, indexService, _) = BuildSubscribed();

        indexService.Fire(GameIndex.Empty);

        Assert.Empty(published);
    }

    [Fact]
    public void IndexChanged_DocumentRemoved_ClearsDiagnostics_For_That_Uri()
    {
        var (_, published, indexService, _) = BuildSubscribed();

        // First fire: a.xml is present
        indexService.Fire(IndexWithDoc("file:///a.xml"));
        published.Clear();

        // Second fire: a.xml is gone
        indexService.Fire(GameIndex.Empty);

        var clear = Assert.Single(published);
        Assert.Equal("file:///a.xml", clear.Uri.ToString());
        Assert.Empty(clear.Diagnostics!);
    }

    // ── duplicate-ID (index-level) ────────────────────────────────────────────

    [Fact]
    public void CollectDuplicateIdDiagnostics_SameId_TwoFiles_EmitsDiagnostic_For_RequestedFile()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var index = IndexWithDuplicateId("UNIT_A", "file:///a.xml", "file:///b.xml");

        var diags = publisher.CollectDuplicateIdDiagnostics("file:///a.xml", index);

        var d = Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("UNIT_A", d.Message);
    }

    [Fact]
    public void CollectDuplicateIdDiagnostics_SameId_TwoFiles_BothFilesGetDiagnostic()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var index = IndexWithDuplicateId("UNIT_A", "file:///a.xml", "file:///b.xml");

        var diagsA = publisher.CollectDuplicateIdDiagnostics("file:///a.xml", index);
        var diagsB = publisher.CollectDuplicateIdDiagnostics("file:///b.xml", index);

        Assert.Single(diagsA);
        Assert.Single(diagsB);
    }

    [Fact]
    public void CollectDuplicateIdDiagnostics_UniqueId_NoDiagnostic()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var sym = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin("file:///a.xml", 0, null), null);
        var doc = new DocumentIndex("file:///a.xml", 1, [sym], []);
        var index = new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add("file:///a.xml", doc),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty
                .Add("UNIT_A", ImmutableArray.Create(sym)),
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty);

        var diags = publisher.CollectDuplicateIdDiagnostics("file:///a.xml", index);

        Assert.Empty(diags);
    }

    // ── unresolved-reference (index-level) ───────────────────────────────────

    [Fact]
    public void CollectUnresolvedRefDiagnostics_MissingTarget_EmitsDiagnostic()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var index = IndexWithRef("file:///a.xml", "UNIT_MISSING", "Unit");

        var diags = publisher.CollectUnresolvedRefDiagnostics("file:///a.xml", index);

        var d = Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("UNIT_MISSING", d.Message);
    }

    [Fact]
    public void CollectUnresolvedRefDiagnostics_Workspace_Resolved_NoDiagnostic()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var baseIndex = IndexWithRef("file:///a.xml", "UNIT_TARGET", "Unit");
        // Add the target to workspace definitions so Resolve returns it
        var resolvedSym = new GameSymbol("UNIT_TARGET", GameSymbolKind.XmlObject, "Unit",
            new FileOrigin("file:///b.xml", 0, null), null);
        var index = baseIndex with
        {
            WorkspaceDefinitions = baseIndex.WorkspaceDefinitions
                .Add("UNIT_TARGET", ImmutableArray.Create(resolvedSym))
        };

        var diags = publisher.CollectUnresolvedRefDiagnostics("file:///a.xml", index);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectUnresolvedRefDiagnostics_Baseline_Resolved_NoDiagnostic()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var baseIndex = IndexWithRef("file:///a.xml", "UNIT_BASELINE");
        var baselineSym = new GameSymbol("UNIT_BASELINE", GameSymbolKind.XmlObject, "Unit",
            new UnknownOrigin("baseline"), null);
        var index = baseIndex with
        {
            Baseline = new BaselineIndex(
                ImmutableDictionary<string, GameSymbol>.Empty.Add("UNIT_BASELINE", baselineSym),
                DateTimeOffset.UtcNow,
                "hash",
                ImmutableDictionary<string, ImmutableArray<string>>.Empty,
                ImmutableDictionary<string, ImmutableArray<string>>.Empty)
        };

        var diags = publisher.CollectUnresolvedRefDiagnostics("file:///a.xml", index);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectUnresolvedRefDiagnostics_OtherDocument_NotReported_For_RequestedUri()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        // Reference is in b.xml, not a.xml
        var reference = new GameReference("UNIT_MISSING", GameSymbolKind.XmlObject, null,
            "file:///b.xml", 0, 0, 12);
        var docB = new DocumentIndex("file:///b.xml", 1, [], [reference]);
        var index = new GameIndex(
            BaselineIndex.Empty,
            ImmutableDictionary<string, DocumentIndex>.Empty.Add("file:///b.xml", docB),
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty,
            ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add("UNIT_MISSING", ImmutableArray.Create(reference)));

        // Asking for a.xml — should have no diagnostics
        var diags = publisher.CollectUnresolvedRefDiagnostics("file:///a.xml", index);

        Assert.Empty(diags);
    }

    // ── enum boundary diagnostics ────────────────────────────────────────────

    private static GameIndex IndexWithHardcodedEnums(
        ImmutableDictionary<string, ImmutableArray<string>> hardcoded)
    {
        var baseline = new BaselineIndex(
            ImmutableDictionary<string, GameSymbol>.Empty,
            DateTimeOffset.UtcNow, "hash",
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            hardcoded);
        return GameIndex.Empty with { Baseline = baseline };
    }

    [Fact]
    public void CollectEnumBoundaryDiagnostics_NonGameConstantsFile_NoWarning()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var hardcoded = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("DamageType", ["EXPLOSIVE"]);
        var index = IndexWithHardcodedEnums(hardcoded);

        const string xml = """
            <GameConstants>
              <Damage_Types>NEW_TYPE
            <!-- PLEASE add your new damage types ABOVE this point. -->
            EXPLOSIVE
              </Damage_Types>
            </GameConstants>
            """;

        var diags = publisher.CollectEnumBoundaryDiagnostics("file:///units.xml", xml, index);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectEnumBoundaryDiagnostics_EmptyHardcodedBaseline_NoWarning()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var index = IndexWithHardcodedEnums(
            ImmutableDictionary<string, ImmutableArray<string>>.Empty);

        const string xml = """
            <GameConstants>
              <Damage_Types>CUSTOM
            <!-- PLEASE add your new damage types ABOVE this point. -->
            MYSTERY_TYPE
              </Damage_Types>
            </GameConstants>
            """;

        var diags = publisher.CollectEnumBoundaryDiagnostics("file:///data/xml/gameconstants.xml", xml, index);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectEnumBoundaryDiagnostics_NoBoundaryComment_NoWarning()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var hardcoded = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("DamageType", ["EXPLOSIVE"]);
        var index = IndexWithHardcodedEnums(hardcoded);

        const string xml = "<GameConstants><Damage_Types>EXPLOSIVE ENERGY CUSTOM</Damage_Types></GameConstants>";

        var diags = publisher.CollectEnumBoundaryDiagnostics("file:///data/xml/gameconstants.xml", xml, index);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectEnumBoundaryDiagnostics_KnownHardcodedToken_NoWarning()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var hardcoded = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("DamageType", ["EXPLOSIVE", "ENERGY"]);
        var index = IndexWithHardcodedEnums(hardcoded);

        const string xml = """
            <GameConstants>
              <Damage_Types>CUSTOM_TYPE
            <!-- PLEASE add your new damage types ABOVE this point. -->
            EXPLOSIVE ENERGY
              </Damage_Types>
            </GameConstants>
            """;

        var diags = publisher.CollectEnumBoundaryDiagnostics("file:///data/xml/gameconstants.xml", xml, index);

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectEnumBoundaryDiagnostics_MisplacedToken_EmitsWarning()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var hardcoded = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("DamageType", ["EXPLOSIVE", "ENERGY"]);
        var index = IndexWithHardcodedEnums(hardcoded);

        // SABER_SLASH was added below the boundary — not in the hardcoded set
        const string xml = """
            <GameConstants>
              <Damage_Types>CUSTOM_TYPE
            <!-- PLEASE add your new damage types ABOVE this point. -->
            EXPLOSIVE SABER_SLASH ENERGY
              </Damage_Types>
            </GameConstants>
            """;

        var diags = publisher.CollectEnumBoundaryDiagnostics("file:///data/xml/gameconstants.xml", xml, index);

        Assert.Single(diags);
        Assert.Contains("SABER_SLASH", diags[0].Message);
        Assert.Equal(DiagnosticSeverity.Warning, diags[0].Severity);
    }

    [Fact]
    public void CollectEnumBoundaryDiagnostics_MultipleEnums_BothChecked()
    {
        var (publisher, _, _, _) = BuildSubscribed();
        var hardcoded = ImmutableDictionary<string, ImmutableArray<string>>.Empty
            .Add("DamageType", ["EXPLOSIVE"])
            .Add("ArmorType",  ["ARMOR_INFANTRY"]);
        var index = IndexWithHardcodedEnums(hardcoded);

        const string xml = """
            <GameConstants>
              <Damage_Types>CUSTOM
            <!-- PLEASE add your new damage types ABOVE this point. -->
            EXPLOSIVE MOD_DAMAGE_BELOW
              </Damage_Types>
              <Armor_Types>MY_ARMOR
            <!-- PLEASE add your new armor types ABOVE this point. -->
            ARMOR_INFANTRY MOD_ARMOR_BELOW
              </Armor_Types>
            </GameConstants>
            """;

        var diags = publisher.CollectEnumBoundaryDiagnostics("file:///data/xml/gameconstants.xml", xml, index);

        Assert.Equal(2, diags.Count);
        Assert.Contains(diags, d => d.Message.Contains("MOD_DAMAGE_BELOW"));
        Assert.Contains(diags, d => d.Message.Contains("MOD_ARMOR_BELOW"));
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeSchemaProvider : ISchemaProvider
    {
        private readonly Dictionary<string, XmlTagDefinition> _tags = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GameObjectTypeDefinition> _types = new(StringComparer.OrdinalIgnoreCase);

        public XmlTagDefinition? GetTag(string name)
        {
            return _tags.GetValueOrDefault(name);
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
        {
            return [];
        }

        public IReadOnlyList<XmlTagDefinition> AllTags => [.. _tags.Values];

        public GameObjectTypeDefinition? GetObjectType(string name)
        {
            return _types.GetValueOrDefault(name);
        }

        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [.. _types.Values];

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
        {
            return [];
        }

        public EnumDefinition? GetEnum(string _)
        {
            return null;
        }

        public IReadOnlyList<EnumDefinition> AllEnums => [];

        public event EventHandler? SchemaRefreshed
        {
            add { }
            remove { }
        }

        public void AddTag(XmlTagDefinition tag)
        {
            _tags[tag.Tag] = tag;
        }

        public void AddType(GameObjectTypeDefinition type)
        {
            _types[type.TypeName] = type;
        }
    }

    private sealed class FakeValidatorRegistry : IXmlValueValidatorRegistry
    {
        public XmlValidationResult Validate(XmlValueType _, string __, XmlTagDefinition ___)
        {
            return XmlValidationResult.Valid();
        }
    }

    private sealed class FakeGameIndexService : IGameIndexService
    {
        public GameIndex Current => GameIndex.Empty;
        public event Action<GameIndex>? IndexChanged;
        public void Fire(GameIndex index) => IndexChanged?.Invoke(index);
        public Task UpdateDocumentAsync(string uri, string text, int version, CancellationToken ct) => Task.CompletedTask;
        public void RemoveDocument(string uri) { }
        public void ApplyBaseline(BaselineIndex baseline) { }
    }

    private sealed class FakeGameWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = [];

        public void Set(string uri, string text, int version = 1)
            => _docs[uri] = new TrackedDocument(uri, text, version);

        public void AddOrUpdate(string uri, string text, int version) => Set(uri, text, version);
        public void Remove(string uri) => _docs.Remove(uri);

        public bool TryGet(string uri, out TrackedDocument doc)
            => _docs.TryGetValue(uri, out doc!);

        public IEnumerable<TrackedDocument> All => _docs.Values;
    }
}
