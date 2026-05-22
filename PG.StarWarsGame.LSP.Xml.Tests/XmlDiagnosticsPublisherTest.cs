// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Validation;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class XmlDiagnosticsPublisherTest
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
    private static XmlDiagnosticsPublisher BuildPublisher(FakeSchemaProvider schema,
        FakeFileTypeRegistry? registry = null)
    {
        return new XmlDiagnosticsPublisher(
            _ => { },
            new FakeGameIndexService(),
            new FakeGameWorkspaceHost(),
            schema,
            new FakeValidatorRegistry(),
            new StoryParserDiagnosticCollector(schema),
            NullLogger<XmlDiagnosticsPublisher>.Instance,
            registry ?? new FakeFileTypeRegistry());
    }

    // Subscription / routing tests use this builder so they can control the index service.
    private static (XmlDiagnosticsPublisher publisher,
        List<PublishDiagnosticsParams> published,
        FakeGameIndexService indexService,
        FakeGameWorkspaceHost workspaceHost) BuildSubscribed(FakeSchemaProvider? schema = null,
        FakeFileTypeRegistry? registry = null)
    {
        var published = new List<PublishDiagnosticsParams>();
        var indexService = new FakeGameIndexService();
        var workspaceHost = new FakeGameWorkspaceHost();
        var effectiveSchema = schema ?? new FakeSchemaProvider();
        var publisher = new XmlDiagnosticsPublisher(
            p => published.Add(p),
            indexService,
            workspaceHost,
            effectiveSchema,
            new FakeValidatorRegistry(),
            new StoryParserDiagnosticCollector(effectiveSchema),
            NullLogger<XmlDiagnosticsPublisher>.Instance,
            registry ?? new FakeFileTypeRegistry());
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
        schema.AddType(new GameObjectTypeDefinition { TypeName = "SpaceUnit", NameTag = "Name" });

        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("SpaceUnit"));
        var publisher = BuildPublisher(schema, registry);

        // Two separate SpaceUnit type containers, each with one Max_Speed — not duplicates
        var diags = publisher.CollectDiagnostics("""
                                                 <GameObjectFiles>
                                                   <SpaceUnit Name="UnitA">
                                                     <Max_Speed>500</Max_Speed>
                                                   </SpaceUnit>
                                                   <SpaceUnit Name="UnitB">
                                                     <Max_Speed>300</Max_Speed>
                                                   </SpaceUnit>
                                                 </GameObjectFiles>
                                                 """, DocumentUri.From("file:///test.xml"));

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
        var publisher = BuildPublisher(schema);

        // Two Faction elements at document root — individually iterated, never compared against each other
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

    // ── depth-based type-container detection ────────────────────────────────────

    [Fact]
    public void CollectDiagnostics_MultiInstance_TypeContainerNameMatchesFieldTag_NoDuplicateFalsePositive()
    {
        // "Faction" is registered as a field tag AND appears as a type-container element name.
        // Two type containers with the same element name must not be flagged as duplicate singletons.
        var schema = new FakeSchemaProvider();
        schema.AddTag(MakeTag("Faction")); // not MultipleAllowed
        schema.AddType(new GameObjectTypeDefinition { TypeName = "FactionType", NameTag = "Name" });

        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("FactionType"));
        var publisher = BuildPublisher(schema, registry);

        var diags = publisher.CollectDiagnostics("""
                                                 <GameObjectFiles>
                                                   <Faction Name="EMPIRE"><Name>EMPIRE</Name></Faction>
                                                   <Faction Name="REBEL"><Name>REBEL</Name></Faction>
                                                 </GameObjectFiles>
                                                 """, DocumentUri.From("file:///test.xml"));

        Assert.Empty(diags);
    }

    [Fact]
    public void CollectDiagnostics_MultiInstance_FieldTagsInsideTypeContainer_StillValidated()
    {
        // Even though depth-1 elements are type containers, their children (field tags) are still validated.
        var schema = new FakeSchemaProvider();
        schema.AddType(new GameObjectTypeDefinition { TypeName = "SpaceUnit", NameTag = "Name" });
        schema.AddTag(MakeTag("Max_Speed"));

        var registry = new FakeFileTypeRegistry();
        registry.Register("test.xml", ImmutableArray.Create("SpaceUnit"));
        var publisher = BuildPublisher(schema, registry);

        // Two Max_Speed tags inside a single type container — duplicate singleton error expected.
        var diags = publisher.CollectDiagnostics("""
                                                 <GameObjectFiles>
                                                   <SpaceUnit Name="UnitA">
                                                     <Max_Speed>100</Max_Speed>
                                                     <Max_Speed>200</Max_Speed>
                                                   </SpaceUnit>
                                                 </GameObjectFiles>
                                                 """, DocumentUri.From("file:///test.xml"));

        Assert.Equal(2, diags.Count);
        Assert.All(diags, d => Assert.Contains("Max_Speed", d.Message));
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
    public void IndexChanged_Fires_Publishes_For_Each_OpenDocument()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.Set("file:///a.xml", "<Root/>");

        indexService.Fire(IndexWithDoc("file:///a.xml"));

        Assert.Single(published);
        Assert.Equal("file:///a.xml", published[0].Uri.ToString());
    }

    [Fact]
    public void IndexChanged_TwoOpenDocuments_PublishesTwice()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.Set("file:///a.xml", "<Root/>");
        workspaceHost.Set("file:///b.xml", "<Root/>");

        var index = IndexWithDoc("file:///a.xml") with
        {
            Documents = IndexWithDoc("file:///a.xml").Documents
                .Add("file:///b.xml", new DocumentIndex("file:///b.xml", 1, [], []))
        };
        indexService.Fire(index);

        Assert.Equal(2, published.Count);
    }

    [Fact]
    public void IndexChanged_NoOpenDocuments_PublishesNothing()
    {
        var (_, published, indexService, _) = BuildSubscribed();

        indexService.Fire(GameIndex.Empty);

        Assert.Empty(published);
    }

    [Fact]
    public void IndexChanged_DocumentClosed_ClearsDiagnostics_For_That_Uri()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.Set("file:///a.xml", "<Root/>");

        // First fire while the file is open: diagnostics published.
        indexService.Fire(IndexWithDoc("file:///a.xml"));
        published.Clear();

        // Simulate DidClose: remove from workspace host, then IndexChanged fires.
        workspaceHost.Remove("file:///a.xml");
        indexService.Fire(GameIndex.Empty);

        var clear = Assert.Single(published);
        Assert.Equal("file:///a.xml", clear.Uri.ToString());
        Assert.Empty(clear.Diagnostics!);
    }

    [Fact]
    public void IndexChanged_OnlyPublishesForOpenFiles_NotForIndexOnlyEntries()
    {
        // Regression: WorkspaceScanner stores filesystem paths as index keys while the
        // LSP sync handler stores file:// URIs. The two can coexist as separate keys.
        // OnIndexChanged must publish only for files open in the workspace host;
        // scanner-only entries must not generate any publication (including empty).
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.Set("file:///a.xml", "<Root/>");

        // Fire with two index keys: the workspace-host file:// URI plus a "scanner path"
        // key that represents the same file under a different string.
        var indexWithBoth = IndexWithDoc("file:///a.xml") with
        {
            Documents = IndexWithDoc("file:///a.xml").Documents
                .Add("a.xml", new DocumentIndex("a.xml", 0, [], []))
        };
        indexService.Fire(indexWithBoth);

        // Only one publish — for the open file. The scanner-path key must not generate
        // a second (empty) publication that would clear the editor's diagnostics.
        Assert.Single(published);
        Assert.Equal("file:///a.xml", published[0].Uri.ToString());
    }

    [Fact]
    public void IndexChanged_ScannerFiresAfterOpen_StillPublishesForOpenFile()
    {
        // Regression: scanner fires IndexChanged with filesystem-path keys only; the
        // editor's file:// URI must still be published after the scan (not silently dropped).
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        workspaceHost.Set("file:///a.xml", "<Root/>");

        // First fire (DidOpen equivalent): only the file:// URI is in the index.
        indexService.Fire(IndexWithDoc("file:///a.xml"));
        published.Clear();

        // Second fire (scan equivalent): only the filesystem-path key is in the index.
        var scanIndex = GameIndex.Empty with
        {
            Documents = System.Collections.Immutable.ImmutableDictionary<string, DocumentIndex>.Empty
                .Add("a.xml", new DocumentIndex("a.xml", 0, [], []))
        };
        indexService.Fire(scanIndex);

        // The open file must still be published — diagnostics for open files are always
        // refreshed on every IndexChanged regardless of whether the index uses a different
        // key for the same physical file.
        var pub = Assert.Single(published);
        Assert.Equal("file:///a.xml", pub.Uri.ToString());
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
            hardcoded,
            ImmutableDictionary<string, ImmutableArray<string>>.Empty);
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
            .Add("ArmorType", ["ARMOR_INFANTRY"]);
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

    // ── story parser guard (root-element detection) ───────────────────────────

    [Fact]
    public void OnIndexChanged_StoryParserDocument_EmitsStoryDiagnostics()
    {
        // ZOOM_IN has 0 params; Reward_Param1 is spurious → story collector emits a warning.
        var registry = new FakeFileTypeRegistry();
        registry.Register("storyplots.xml", ImmutableArray.Create("StoryParser"));
        var schema = new FakeSchemaProvider();
        schema.AddType(new GameObjectTypeDefinition { TypeName = "StoryParser" });
        schema.AddEnum(new EnumDefinition
        {
            Name = "StoryRewardType", Kind = EnumKind.SchemaFixed,
            Values = [new EnumValueDefinition { Name = "ZOOM_IN", Params = [] }]
        });
        var (_, published, indexService, workspaceHost) = BuildSubscribed(schema, registry);

        const string uri = "file:///StoryPlots.xml";
        const string xml = "<StoryParser><Event>" +
                           "<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                           "<Reward_Type>ZOOM_IN</Reward_Type>" +
                           "<Reward_Param1>extra</Reward_Param1>" +
                           "</Event></StoryParser>";

        workspaceHost.Set(uri, xml);
        indexService.Fire(IndexWithDoc(uri));

        var storyDiags = published.FirstOrDefault(p => p.Uri.ToString() == uri)?.Diagnostics;
        Assert.NotNull(storyDiags);
        Assert.Contains(storyDiags, d => d.Message.Contains("Reward_Param1") && d.Message.Contains("ZOOM_IN"));
    }

    [Fact]
    public void OnIndexChanged_NonStoryParserDocument_EmitsNoStoryDiagnostics()
    {
        // Faction root — should never trigger story validation even if it has <Event_Type> children.
        var schema = new FakeSchemaProvider();
        schema.AddType(new GameObjectTypeDefinition { TypeName = "Faction" });
        var (_, published, indexService, workspaceHost) = BuildSubscribed(schema);

        const string uri = "file:///Factions.xml";
        const string xml = "<Faction>" +
                           "<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                           "</Faction>";

        workspaceHost.Set(uri, xml);
        indexService.Fire(IndexWithDoc(uri));

        var storyDiags = published.FirstOrDefault(p => p.Uri.ToString() == uri)?.Diagnostics;
        Assert.NotNull(storyDiags);
        Assert.DoesNotContain(storyDiags, d => d.Message.Contains("is not used by") || d.Message.Contains("requires"));
    }

    // ── StoryParser registry-based detection ─────────────────────────────────

    [Fact]
    public void OnIndexChanged_StoryParserDocument_RegistryMatch_ArbitraryRootElement_EmitsStoryDiagnostics()
    {
        // Detection must use the registry, not the root element name.
        var registry = new FakeFileTypeRegistry();
        const string uri = "file:///StoryPlots.xml";
        registry.Register("storyplots.xml", ImmutableArray.Create("StoryParser"));
        var schema = new FakeSchemaProvider();
        schema.AddType(new GameObjectTypeDefinition { TypeName = "StoryParser" });
        schema.AddEnum(new EnumDefinition
        {
            Name = "StoryRewardType", Kind = EnumKind.SchemaFixed,
            Values = [new EnumValueDefinition { Name = "ZOOM_IN", Params = [] }]
        });
        var (_, published, indexService, workspaceHost) = BuildSubscribed(schema, registry);

        const string xml = "<SomeContainer><Event>" +
                           "<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                           "<Reward_Type>ZOOM_IN</Reward_Type>" +
                           "<Reward_Param1>extra</Reward_Param1>" +
                           "</Event></SomeContainer>";

        workspaceHost.Set(uri, xml);
        indexService.Fire(IndexWithDoc(uri));

        var storyDiags = published.FirstOrDefault(p => p.Uri.ToString() == uri)?.Diagnostics;
        Assert.NotNull(storyDiags);
        Assert.Contains(storyDiags, d => d.Message.Contains("Reward_Param1") && d.Message.Contains("ZOOM_IN"));
    }

    [Fact]
    public void OnIndexChanged_StoryParserRootElement_NotInRegistry_NoStoryDiagnostics()
    {
        // Root element named "StoryParser" but the file is not registered → no story diagnostics.
        var schema = new FakeSchemaProvider();
        schema.AddType(new GameObjectTypeDefinition { TypeName = "StoryParser" });
        var (_, published, indexService, workspaceHost) = BuildSubscribed(schema);

        const string uri = "file:///StoryPlots.xml";
        const string xml = "<StoryParser><Event>" +
                           "<Event_Type>STORY_MOVIE_DONE</Event_Type>" +
                           "<Reward_Type>ZOOM_IN</Reward_Type>" +
                           "<Reward_Param1>extra</Reward_Param1>" +
                           "</Event></StoryParser>";

        workspaceHost.Set(uri, xml);
        indexService.Fire(IndexWithDoc(uri));

        var storyDiags = published.FirstOrDefault(p => p.Uri.ToString() == uri)?.Diagnostics;
        Assert.NotNull(storyDiags);
        Assert.DoesNotContain(storyDiags, d => d.Message.Contains("Reward_Param1"));
    }

    // ── tag Notes hints ──────────────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_TagWithNotes_EmitsHintDiagnostic()
    {
        var schema = new FakeSchemaProvider();
        schema.AddTag(new XmlTagDefinition
        {
            Tag = "Old_Tag",
            ValueType = XmlValueType.Float,
            Notes = new Dictionary<string, string> { ["en"] = "Never used in vanilla." }
        });
        var (_, published, indexService, workspaceHost) = BuildSubscribed(schema);

        const string uri = "file:///test.xml";
        const string xml = "<Root><Old_Tag>500</Old_Tag></Root>";

        workspaceHost.Set(uri, xml);
        indexService.Fire(IndexWithDoc(uri));

        var diags = published.FirstOrDefault(p => p.Uri.ToString() == uri)?.Diagnostics;
        Assert.NotNull(diags);
        Assert.Contains(diags,
            d => d.Severity == DiagnosticSeverity.Hint && d.Message.Contains("Never used in vanilla."));
    }

    // ── closed-file suppression ──────────────────────────────────────────────

    [Fact]
    public void OnIndexChanged_ClosedFileWithDuplicateId_PublishesNothing()
    {
        // Files not open in the workspace host must never receive any publication —
        // not even an empty one. The editor only knows about open files.
        var (_, published, indexService, _) = BuildSubscribed();
        var index = IndexWithDuplicateId("UNIT_A", "file:///a.xml", "file:///b.xml");

        indexService.Fire(index);

        Assert.Empty(published);
    }

    [Fact]
    public void OnIndexChanged_OpenFileWithDuplicateId_PublishesDuplicateIdDiagnostic()
    {
        var (_, published, indexService, workspaceHost) = BuildSubscribed();
        var index = IndexWithDuplicateId("UNIT_A", "file:///a.xml", "file:///b.xml");

        workspaceHost.Set("file:///a.xml", "<Unit><Name>UNIT_A</Name></Unit>");
        workspaceHost.Set("file:///b.xml", "<Unit><Name>UNIT_A</Name></Unit>");

        indexService.Fire(index);

        Assert.Equal(2, published.Count);
        Assert.All(published, p => Assert.Contains(p.Diagnostics!, d => d.Message.Contains("UNIT_A")));
    }

    [Fact]
    public void OnIndexChanged_ClosedFileWithUnresolvedRef_PublishesNothing()
    {
        // Same reasoning as the duplicate-ID case: closed files must not be published.
        var (_, published, indexService, _) = BuildSubscribed();
        var index = IndexWithRef("file:///a.xml", "UNIT_MISSING");

        indexService.Fire(index);

        Assert.Empty(published);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeSchemaProvider : ISchemaProvider
    {
        private readonly Dictionary<string, XmlTagDefinition> _tags = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GameObjectTypeDefinition> _types = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, EnumDefinition> _enums = new(StringComparer.OrdinalIgnoreCase);

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

        public EnumDefinition? GetEnum(string name) => _enums.GetValueOrDefault(name);

        public IReadOnlyList<EnumDefinition> AllEnums => [.. _enums.Values];

        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

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

        public void AddEnum(EnumDefinition enumDef) => _enums[enumDef.Name] = enumDef;
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

        public void Fire(GameIndex index)
        {
            IndexChanged?.Invoke(index);
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class FakeFileTypeRegistry : IFileTypeRegistry
    {
        private readonly Dictionary<string, ImmutableArray<string>> _map =
            new(StringComparer.OrdinalIgnoreCase);

        public void Register(string key, ImmutableArray<string> types) => _map[key] = types;

        public ImmutableArray<string> GetTypesForFile(string normalizedPath) =>
            _map.TryGetValue(normalizedPath, out var types) ? types : ImmutableArray<string>.Empty;

        public void RegisterFile(string normalizedPath, ImmutableArray<string> typeNames) =>
            _map[normalizedPath] = typeNames;

        public void UnregisterFile(string normalizedPath) => _map.Remove(normalizedPath);

        public IReadOnlyDictionary<string, ImmutableArray<string>> All => _map;
    }

    private sealed class FakeGameWorkspaceHost : IGameWorkspaceHost
    {
        private readonly Dictionary<string, TrackedDocument> _docs = [];

        public void AddOrUpdate(string uri, string text, int version)
        {
            Set(uri, text, version);
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

        public void Set(string uri, string text, int version = 1)
        {
            _docs[uri] = new TrackedDocument(uri, text, version);
        }
    }
}