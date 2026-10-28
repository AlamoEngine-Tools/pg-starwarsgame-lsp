// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.ModBrowser;
using PG.StarWarsGame.LSP.Xml;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Server.Tests.ModBrowser;

/// <summary>
///     An entry's preview (#270): the tiles its type's adapter lists, in order, each sized in grid
///     cells - answered by the server so every adapter is tested here, rendered by the webview.
/// </summary>
public sealed class SymbolPreviewsTest
{
    private const string Units = "file:///c:/work/mod/data/xml/units.xml";
    private const string Page = "xml:GameObjectType";
    private const string Script = "file:///c:/work/mod/data/scripts/gameobject/myhero.lua";
    private const string Model = "data/art/models/ev_stardestroyer.alo";
    private const string Engine = "data/art/models/p_engine.alo";
    private const string Idle = "data/art/models/ev_stardestroyer_idle_00.ala";
    private const string Crowded = "data/art/models/ev_crowded.alo";

    // A script opens with a comment banner, as the game's own do; its source starts after it.
    private static readonly string s_script = string.Join("\n",
        "-- A banner the source skips",
        "--[[ and a block",
        "     comment ]]",
        "",
        "require(\"PGStateMachine\")",
        "",
        "function Definitions()",
        "end");

    private static readonly string s_units = string.Join("\n",
        "<GameObjectFiles>",
        "  <SpaceUnit Name=\"Star_Destroyer\">",
        "    <Space_Model_Name>EV_StarDestroyer.alo</Space_Model_Name>",
        "    <Encyclopedia_Text>TEXT_SD</Encyclopedia_Text>",
        "    <Max_Speed>2.0</Max_Speed>",
        "  </SpaceUnit>",
        "  <SpaceUnit Name=\"Star_Destroyer_Mk2\">",
        "    <Variant_Of_Existing_Type>Star_Destroyer</Variant_Of_Existing_Type>",
        "    <Max_Speed>3.0</Max_Speed>",
        "  </SpaceUnit>",
        "  <SFXEvent Name=\"Unit_Select_SD\">",
        "    <Samples>U_Select_1.wav</Samples>",
        "  </SFXEvent>",
        "</GameObjectFiles>");

    private static readonly Dictionary<string, XmlTagDefinition> s_tags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space_Model_Name"] = Tag("Space_Model_Name", ReferenceKind.ModelFile),
        ["Samples"] = Tag("Samples", ReferenceKind.AudioFile),
        ["Max_Speed"] = Tag("Max_Speed", ReferenceKind.None)
    };

    private static FakeSchemaProvider Schema()
    {
        var builder = new SchemaProviderBuilder()
            .WithKind(new ObjectKindDefinition { Kind = ObjectKindNames.Planet, Behaviors = ["PLANET"] });
        foreach (var tag in s_tags.Values)
            builder.WithTag(tag);
        return builder.Build();
    }

    private static XmlTagDefinition Tag(string name, ReferenceKind kind)
    {
        return new XmlTagDefinition { Tag = name, ValueType = XmlValueType.NameReference, ReferenceKind = kind };
    }

    private sealed class Tags : IVariantTagSource
    {
        private static readonly Dictionary<string, (string Tag, string Value)[]> s_byObject = new()
        {
            ["Star_Destroyer"] =
            [
                ("Space_Model_Name", "EV_StarDestroyer.alo"), ("Encyclopedia_Text", "TEXT_SD"), ("Max_Speed", "2.0")
            ],
            ["Star_Destroyer_Mk2"] = [("Variant_Of_Existing_Type", "Star_Destroyer"), ("Max_Speed", "3.0")],
            ["Unit_Select_SD"] = [("Samples", "U_Select_1.wav")],
            ["Coruscant"] = [("Encyclopedia_Text", "TEXT_CORUSCANT")]
        };

        public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
        {
            return s_byObject.TryGetValue(objectId, out var found)
                ? found.Select((t, i) => new VariantTag(t.Tag, t.Value, $"<{t.Tag}>{t.Value}</{t.Tag}>", i)).ToList()
                : null;
        }
    }

    private sealed class Texts : IDocumentTextSource
    {
        public DocumentText? GetText(string canonicalUri)
        {
            return canonicalUri switch
            {
                Units => new DocumentText(s_units, 1, false),
                Script => new DocumentText(s_script, 1, false),
                _ => null
            };
        }
    }

    // One problem inside Star_Destroyer's definition (line 4), one inside the SFX event (line 11).
    private sealed class Diagnostics : IXmlDiagnosticsCollector
    {
        public IReadOnlyList<Diagnostic> Collect(string uri, string text, GameIndex index)
        {
            return
            [
                Problem(4, "Max_Speed is out of range", DiagnosticSeverity.Error),
                Problem(11, "Missing sample: U_Select_1.wav", DiagnosticSeverity.Warning),
                new Diagnostic
                {
                    Range = new LspRange(new Position(3, 4), new Position(3, 10)),
                    Severity = DiagnosticSeverity.Hint, Message = "A hint is not a problem"
                }
            ];
        }

        private static Diagnostic Problem(int line, string message, DiagnosticSeverity severity)
        {
            return new Diagnostic
            {
                Range = new LspRange(new Position(line, 4), new Position(line, 12)),
                Severity = severity, Message = message
            };
        }
    }

    private static GameSymbol Object(string id, int line, string type, string? variantOf = null)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, type, new FileOrigin(Units, line, 2), null)
        {
            VariantBaseId = variantOf
        };
    }

    private static SymbolPreviews Previews()
    {
        GameSymbol[] symbols =
        [
            Object("Star_Destroyer", 1, "GameObjectType"),
            Object("Star_Destroyer_Mk2", 6, "GameObjectType", "Star_Destroyer"),
            Object("Unit_Select_SD", 10, "SFXEvent"),
            // A planet whose text defines a body, as the vanilla planets' do.
            new GameSymbol("Coruscant", GameSymbolKind.XmlObject, "GameObjectType", new FileOrigin(Units, 13, 2), null,
                null, ["PLANET"])
        ];
        var shipped = new GameSymbol("Shipped_Only", GameSymbolKind.XmlObject, "GameObjectType",
            new FileOrigin(@"DATA\XML\UNITS.XML", 12, 4), null);
        var index = GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with
            {
                Symbols = ImmutableDictionary<string, GameSymbol>.Empty.Add(shipped.Id, shipped)
            },
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(Units, new DocumentIndex(Units, 1, [.. symbols], [], LayerRank: 0, LayerName: "Mod"))
                .Add(Script, new DocumentIndex(Script, 1, [], [], ["PGStateMachine"], LayerRank: 0, LayerName: "Mod")),
            WorkspaceDefinitions = symbols.ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s)),
            AssetFiles = MergedAssetFileIndex.Merge([Model, Engine, Idle, Crowded, "data/art/textures/sd.dds"], []),
            // The model's three bones and two textures, one of which no file holds - the other is
            // named as a .tga the project ships as a .dds, which the game takes for the same texture;
            // the engine effect's one texture.
            ModelBones = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                // Every name a bone reference resolves to: its three bones, and a mesh named apart
                // from any bone, at whose origin the engine places one.
                .Add(ModelBoneKey.From(Model), ["Root", "HP_Weapon_00", "HP_Engine_00", "Hull_Mesh"]),
            ModelTextures = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .Add(ModelBoneKey.From(Model), ["SD.tga", "sd_missing.dds"])
                .Add(ModelBoneKey.From(Engine), ["sd.dds"])
                // More textures than a list was ever cut at.
                .Add(ModelBoneKey.From(Crowded), [.. Enumerable.Range(0, 25).Select(i => $"crowd_{i:00}.dds")]),
            ParticleSystems = [ModelBoneKey.From(Engine)]
        };
        var schema = Schema();
        var tags = new Tags();
        var catalog = SymbolCatalog.Build(index, [], [], new SchemaProviderBuilder().Build());
        // The model's proxies name the engine effect and one no file holds.
        // The model and the script are files of the project on disk; the engine effect ships packed.
        var relations = new SymbolRelations(index, schema, tags, catalog,
            id => id == Model ? ["P_Engine", "P_Missing"] : [],
            id => id switch
            {
                Model => ModelFile,
                Script => ScriptFile,
                _ => null
            });
        // The model file's own skeleton: three bones.
        return new SymbolPreviews(index, relations, new Texts(), new Diagnostics(), Disk(),
            id => id == Model ? 3 : null, new SchemaProviderBuilder().Build());
    }

    private const string ModelFile = @"C:\work\mod\Data\Art\Models\EV_StarDestroyer.alo";
    private const string ScriptFile = @"C:\work\mod\Data\Scripts\GameObject\MyHero.lua";
    private static readonly DateTime s_created = new(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime s_changed = new(2026, 10, 20, 17, 5, 0, DateTimeKind.Utc);

    private static MockFileSystem Disk()
    {
        MockFileData File(byte[] bytes)
        {
            return new MockFileData(bytes) { CreationTime = s_created, LastWriteTime = s_changed };
        }

        return new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [ModelFile] = File(new byte[1536]),
            [ScriptFile] = File(System.Text.Encoding.UTF8.GetBytes(s_script))
        });
    }

    // Every tile as (kind, x, y, width, height) on the preview's grid.
    private static List<(string Kind, int X, int Y, int Width, int Height)> Layout(GetSymbolPreviewResult preview)
    {
        return preview.Tiles.Select(t => (t.Kind, t.X, t.Y, t.Width, t.Height)).ToList();
    }

    [Fact]
    public void AGameObject_IsPreviewedAsIdentity_Problems_References_AndItsEncyclopediaDownTheRight()
    {
        var preview = Previews().Preview(Page, "Star_Destroyer");

        Assert.Equal("gameObject", preview.Adapter);
        Assert.Equal((6, 6), (preview.Columns, preview.Rows));
        Assert.Equal(
            [
                ("identity", 0, 0, 2, 2), ("problems", 2, 0, 2, 2), ("encyclopedia", 4, 0, 2, 6),
                ("entries", 0, 2, 4, 4)
            ],
            Layout(preview));
        Assert.Equal("referencedBy", preview.Tiles[^1].Relation);
        Assert.Equal("Star_Destroyer", preview.Tiles.Single(t => t.Kind == "encyclopedia").ObjectId);
    }

    // A tile absent for one entry has its fixed place all the same: its cells stay empty, and no
    // other tile grows into them (maintainer, 2026-10-22: as Scout, a fixed tile only ever takes its
    // own space).
    [Fact]
    public void AnAbsentTile_LeavesItsCellsEmpty()
    {
        Assert.Equal(
            [("identity", 0, 0, 2, 2), ("encyclopedia", 4, 0, 2, 6), ("entries", 0, 2, 4, 4)],
            Layout(Previews().Preview(Page, "Star_Destroyer_Mk2")));
    }

    // The definition is the file's, one click away with its highlighting; no preview repeats it
    // (maintainer, 2026-10-22).
    [Fact]
    public void NoPreview_RepeatsTheDefinitionItsFileShows()
    {
        var previews = Previews();

        Assert.All(
            new[]
            {
                previews.Preview(Page, "Star_Destroyer"), previews.Preview(Page, "Star_Destroyer_Mk2"),
                previews.Preview("xml:SFXEvent", "Unit_Select_SD"), previews.Preview("luaScripts", Script)
            },
            p => Assert.DoesNotContain(p.Tiles, t => t.Kind is "definition" or "source"));
    }

    [Fact]
    public void TheIdentity_NamesTheEntry_WhereItIsDefined_AndWhatItIsAVariantOf()
    {
        var identity = Previews().Preview(Page, "Star_Destroyer_Mk2").Tiles[0];

        Assert.Equal("Star_Destroyer_Mk2", identity.Entry?.Name);
        Assert.Equal(new SymbolLocation(Units, 6), identity.Location);
        Assert.Equal("Star_Destroyer", identity.VariantOf);
    }

    [Fact]
    public void Problems_AreTheErrorsAndWarningsInsideTheDefinition_AndAreLeftOutWhenThereAreNone()
    {
        var previews = Previews();

        var problems = previews.Preview(Page, "Star_Destroyer").Tiles.Single(t => t.Kind == "problems");

        Assert.Equal(["Max_Speed is out of range"], problems.Entries.Select(e => e.Name));
        Assert.Equal(new SymbolLocation(Units, 4), problems.Entries[0].Location);
        Assert.DoesNotContain(previews.Preview(Page, "Star_Destroyer_Mk2").Tiles, t => t.Kind == "problems");
    }

    [Fact]
    public void AProblem_CarriesItsSeverity_SoItIsDrawnAsTheProblemsViewDrawsIt()
    {
        var previews = Previews();

        var error = previews.Preview(Page, "Star_Destroyer").Tiles.Single(t => t.Kind == "problems");
        var warning = previews.Preview("xml:SFXEvent", "Unit_Select_SD").Tiles.Single(t => t.Kind == "problems");

        Assert.Equal("error", error.Entries.Single().Severity);
        Assert.Equal("warning", warning.Entries.Single().Severity);
    }

    [Fact]
    public void AnotherXmlObject_ShowsItsOwnRelationsUnderItsHeader_AndItsReferencesDownTheRight()
    {
        var preview = Previews().Preview("xml:SFXEvent", "Unit_Select_SD");

        Assert.Equal("xmlObject", preview.Adapter);
        Assert.Equal(
            [
                ("identity", 0, 0, 2, 2), ("problems", 2, 0, 2, 2), ("entries", 0, 2, 4, 4),
                ("entries", 4, 0, 2, 6)
            ],
            Layout(preview));
        Assert.Equal(["samples", "referencedBy"],
            preview.Tiles.Where(t => t.Kind == "entries").Select(t => t.Relation));
    }

    [Fact]
    public void AnObjectOnlyTheGameDefines_HasItsHeaderAndReferences_AndNoProblems()
    {
        var preview = Previews().Preview(Page, "Shipped_Only");

        Assert.Equal([("identity", 0, 0, 2, 2), ("entries", 0, 2, 4, 4)], Layout(preview));
    }

    // The tile is the card the action shows, so it is there exactly when the action is offered.
    [Fact]
    public void APlanet_HasNoEncyclopediaTile_ThoughItsTextDefinesABody()
    {
        Assert.DoesNotContain(Previews().Preview(Page, "Coruscant").Tiles, t => t.Kind == "encyclopedia");
    }

    // A preview that would be its header alone is none: a texture, a sound, an animation and an enum
    // have nothing to show yet (maintainer, 2026-10-22).
    [Fact]
    public void AnEntryWhosePreviewWouldBeItsHeaderAlone_OrNoEntryAtAll_HasNoPreview()
    {
        var previews = Previews();

        Assert.Null(previews.Preview("textures", "data/art/textures/sd.dds").Adapter);
        Assert.Null(previews.Preview("animations", Idle).Adapter);
        Assert.Null(previews.Preview("models", "nothing.alo").Adapter);
        Assert.Null(previews.Preview(Page, "Nothing_By_This_Name").Adapter);
        Assert.False(SymbolPreviews.HasPreview("animations"));
        Assert.True(SymbolPreviews.HasPreview("models"));
        Assert.True(SymbolPreviews.HasPreview("xml:SFXEvent"));
    }

    [Fact]
    public void AModel_PairsItsListsOnTheLeft_ItsTreeTheRightHalfFullHeight()
    {
        var preview = Previews().Preview("models", Model);

        Assert.Equal("model", preview.Adapter);
        // Four columns at their least width still fit beside the actions. The lists two to a band, two
        // rows tall; the tree - the page's main content, and a deep one - two columns by every row.
        Assert.Equal((4, 6), (preview.Columns, preview.Rows));
        Assert.Equal(
            [
                ("identity", 0, 0, 1, 2), ("problems", 1, 0, 1, 2), ("hierarchy", 2, 0, 2, 6),
                ("entries", 0, 2, 1, 2), ("entries", 1, 2, 1, 2), ("entries", 0, 4, 1, 2), ("entries", 1, 4, 1, 2)
            ],
            Layout(preview));
        // The tree is read from the model by name, as the model preview reads it.
        Assert.Equal("ev_stardestroyer.alo", preview.Tiles.Single(t => t.Kind == "hierarchy").ObjectId);
        Assert.Equal(["textures", "particleSystems", "animations", "usedBy"],
            preview.Tiles.Where(t => t.Kind == "entries").Select(t => t.Relation));
    }

    // Its bones are the file's skeleton, not every name a bone reference resolves to: a mesh named
    // apart from any bone is no bone of the model's (measured on FoC: the Star Destroyer's 71 bones
    // against 74 names).
    [Fact]
    public void AModelsHeader_CountsItsBones_Textures_ParticleSystems_AndAnimations()
    {
        var identity = Previews().Preview("models", Model).Tiles.Single(t => t.Kind == "identity");

        Assert.Equal(
            [
                new PreviewFact("bones", 3), new PreviewFact("textures", 2), new PreviewFact("particleSystems", 2),
                new PreviewFact("animations", 1)
            ],
            identity.Facts);
    }

    [Fact]
    public void AFilesHeader_SaysHowLargeItIs_AndWhenItWasCreatedAndLastChanged()
    {
        var file = Previews().Preview("models", Model).Tiles.Single(t => t.Kind == "identity").File;

        Assert.Equal(new PreviewFile(1536, s_created, s_changed), file);
    }

    [Fact]
    public void AScriptsHeader_AlsoCountsItsLines()
    {
        var file = Previews().Preview("luaScripts", Script).Tiles.Single(t => t.Kind == "identity").File;

        Assert.Equal(8, file?.Lines);
    }

    // A file only the game's archives hold has nothing on disk to describe.
    [Fact]
    public void APackedAssetsHeader_HasNoFileFacts()
    {
        Assert.Null(Previews().Preview("particleSystems", Engine).Tiles.Single(t => t.Kind == "identity").File);
    }

    [Fact]
    public void AModelsProblems_AreTheTexturesAndParticleSystemsItNamesThatNoFileHolds()
    {
        var problems = Previews().Preview("models", Model).Tiles.Single(t => t.Kind == "problems");

        Assert.Equal(2, problems.Count);
        Assert.Equal(
            [("sd_missing.dds", "missingTexture", "warning"), ("p_missing.alo", "missingParticleSystem", "warning")],
            problems.Entries.Select(e => (e.Name, e.Reason, e.Severity)));
    }

    [Fact]
    public void ATextureNamedAsATga_IsTheDdsTheProjectShips_AsTheGameTakesIt()
    {
        var textures = Previews().Preview("models", Model).Tiles.Single(t => t.Relation == "textures");

        Assert.Equal("data/art/textures/sd.dds", textures.Entries[0].Row?.Id);
    }

    [Fact]
    public void AnAssetsLine_HasNoDetail_ItsExtensionBeingAlreadyInItsName()
    {
        var textures = Previews().Preview("models", Model).Tiles.Single(t => t.Relation == "textures");

        Assert.All(textures.Entries, e => Assert.Null(e.Detail));
    }

    // The tile scrolls, so a list is never cut short with a count of what it left out.
    [Fact]
    public void AList_HoldsEveryEntry()
    {
        var textures = Previews().Preview("models", Crowded).Tiles.Single(t => t.Relation == "textures");

        Assert.Equal(25, textures.Count);
        Assert.Equal(25, textures.Entries.Count);
    }

    [Fact]
    public void AParticleSystem_IsPreviewedAsIdentity_Textures_AndReferences()
    {
        var preview = Previews().Preview("particleSystems", Engine);

        Assert.Equal("particleSystem", preview.Adapter);
        Assert.Equal([("identity", 0, 0, 2, 2), ("entries", 0, 2, 4, 4), ("entries", 4, 0, 2, 6)], Layout(preview));
        Assert.Equal(["textures", "usedBy"], preview.Tiles.Where(t => t.Kind == "entries").Select(t => t.Relation));
    }

    [Fact]
    public void ALuaScript_IsItsHeader_LoadedBy_AndRequires_InOneRow()
    {
        var preview = Previews().Preview("luaScripts", Script);

        Assert.Equal("luaScript", preview.Adapter);
        Assert.Equal((6, 2), (preview.Columns, preview.Rows));
        Assert.Equal([("identity", 0, 0, 2, 2), ("entries", 2, 0, 2, 2), ("entries", 4, 0, 2, 2)], Layout(preview));
        Assert.Equal(["loadedBy", "requires"], preview.Tiles.Where(t => t.Kind == "entries").Select(t => t.Relation));
    }
}