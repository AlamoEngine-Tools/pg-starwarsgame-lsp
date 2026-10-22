// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Configuration;
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

    private static XmlTagDefinition Tag(string name, ReferenceKind kind)
    {
        return new XmlTagDefinition { Tag = name, ValueType = XmlValueType.NameReference, ReferenceKind = kind };
    }

    private sealed class Schema : NullSchemaProvider
    {
        public override XmlTagDefinition? GetTag(string tagName)
        {
            return s_tags.GetValueOrDefault(tagName);
        }
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
            ["Unit_Select_SD"] = [("Samples", "U_Select_1.wav")]
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
            return canonicalUri == Units ? new DocumentText(s_units, 1, false) : null;
        }
    }

    // One problem inside Star_Destroyer's definition (line 4), one inside the SFX event (line 11).
    private sealed class Diagnostics : IXmlDiagnosticsCollector
    {
        public IReadOnlyList<Diagnostic> Collect(string uri, string text, GameIndex index)
        {
            return
            [
                Problem(4, "Max_Speed is out of range"),
                Problem(11, "Missing sample: U_Select_1.wav"),
                new Diagnostic
                {
                    Range = new LspRange(new Position(3, 4), new Position(3, 10)),
                    Severity = DiagnosticSeverity.Hint, Message = "A hint is not a problem"
                }
            ];
        }

        private static Diagnostic Problem(int line, string message)
        {
            return new Diagnostic
            {
                Range = new LspRange(new Position(line, 4), new Position(line, 12)),
                Severity = DiagnosticSeverity.Error, Message = message
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

    private static SymbolPreviews Previews(ToolsFeatureFlags? tools = null)
    {
        GameSymbol[] symbols =
        [
            Object("Star_Destroyer", 1, "GameObjectType"),
            Object("Star_Destroyer_Mk2", 6, "GameObjectType", "Star_Destroyer"),
            Object("Unit_Select_SD", 10, "SFXEvent")
        ];
        var shipped = new GameSymbol("Shipped_Only", GameSymbolKind.XmlObject, "GameObjectType",
            new FileOrigin(@"DATA\XML\UNITS.XML", 12, 4), null);
        var index = GameIndex.Empty with
        {
            Baseline = BaselineIndex.Empty with
            {
                Symbols = ImmutableDictionary<string, GameSymbol>.Empty.Add(shipped.Id, shipped)
            },
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty.Add(Units,
                new DocumentIndex(Units, 1, [.. symbols], [], LayerRank: 0, LayerName: "Mod")),
            WorkspaceDefinitions = symbols.ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s)),
            AssetFiles = MergedAssetFileIndex.Merge(["data/art/models/ev_stardestroyer.alo"], [])
        };
        var schema = new Schema();
        var tags = new Tags();
        var catalog = SymbolCatalog.Build(index, [], []);
        var relations = new SymbolRelations(index, schema, tags, catalog, tools: tools);
        return new SymbolPreviews(index, schema, tags, relations, new Texts(), new Diagnostics(),
            tools ?? new ToolsFeatureFlags());
    }

    private static List<(string Kind, int Width, int Height)> Layout(GetSymbolPreviewResult preview)
    {
        return preview.Tiles.Select(t => (t.Kind, t.Width, t.Height)).ToList();
    }

    [Fact]
    public void AGameObject_IsPreviewedAsIdentity_Problems_Encyclopedia_Definition_References()
    {
        var preview = Previews().Preview(Page, "Star_Destroyer");

        Assert.Equal("gameObject", preview.Adapter);
        Assert.Equal(
            [
                ("identity", 2, 2), ("problems", 1, 1), ("encyclopedia", 2, 3), ("definition", 2, 2),
                ("entries", 1, 2)
            ],
            Layout(preview));
        Assert.Equal("referencedBy", preview.Tiles[^1].Relation);
        Assert.Equal("Star_Destroyer", preview.Tiles.Single(t => t.Kind == "encyclopedia").ObjectId);
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
    public void TheDefinition_IsTheElementAsWritten_WithTheEffectiveObjectOnlyForAVariant()
    {
        var previews = Previews();

        var plain = previews.Preview(Page, "Star_Destroyer").Tiles.Single(t => t.Kind == "definition");
        var variant = previews.Preview(Page, "Star_Destroyer_Mk2").Tiles.Single(t => t.Kind == "definition");

        Assert.Equal(string.Join("\n", s_units.Split('\n')[1..6]).Trim(), plain.Text?.Trim());
        Assert.Null(plain.Effective);
        Assert.StartsWith("<SpaceUnit Name=\"Star_Destroyer_Mk2\">", variant.Text);
        Assert.Contains("EV_StarDestroyer.alo", variant.Effective);
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
    public void AnotherXmlObject_ShowsItsOwnRelations_AndNoEncyclopedia()
    {
        var preview = Previews().Preview("xml:SFXEvent", "Unit_Select_SD");

        Assert.Equal("xmlObject", preview.Adapter);
        Assert.Equal(
            [("identity", 2, 2), ("problems", 1, 1), ("entries", 1, 2), ("definition", 2, 2), ("entries", 1, 2)],
            Layout(preview));
        Assert.Equal(["samples", "referencedBy"],
            preview.Tiles.Where(t => t.Kind == "entries").Select(t => t.Relation));
    }

    [Fact]
    public void AnObjectOnlyTheGameDefines_HasNoDefinitionToShow_AndNoProblems()
    {
        var preview = Previews().Preview(Page, "Shipped_Only");

        Assert.Equal([("identity", 2, 2), ("entries", 1, 2)], Layout(preview));
    }

    [Fact]
    public void TheEncyclopedia_IsLeftOutWhenItsFeatureIsOff()
    {
        var preview = Previews(new ToolsFeatureFlags { Encyclopedia = false }).Preview(Page, "Star_Destroyer");

        Assert.DoesNotContain(preview.Tiles, t => t.Kind == "encyclopedia");
    }

    [Fact]
    public void AnEntryWithoutAnAdapter_OrNoEntryAtAll_HasNoPreview()
    {
        var previews = Previews();

        Assert.Null(previews.Preview("models", "ev_stardestroyer.alo").Adapter);
        Assert.Null(previews.Preview(Page, "Nothing_By_This_Name").Adapter);
    }
}