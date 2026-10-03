// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Xml.CodeActions;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;

namespace PG.StarWarsGame.LSP.Xml.Tests.CodeActions;

/// <summary>
///     A "Preview model" action on a value that names a model: a whole model tag anywhere on its
///     line, and a model item of a slotted tuple under the cursor. Not tied to a diagnostic - the
///     model is fine, the reader just wants to look at it.
/// </summary>
public sealed class PreviewModelCodeActionProviderTest
{
    private const string Uri = "file:///test/Units.xml";

    //  line 0: <Units>
    //  line 1: <Unit Name="A">
    //  line 2: <Land_Model_Name>EI_TROOPER.ALO</Land_Model_Name>
    //  line 3: <Land_Terrain_Model_Mapping>Temperate, EI_SNOW.ALO, Desert, EI_SAND.ALO</Land_Terrain_Model_Mapping>
    //  line 4: <Max_Speed>1.0</Max_Speed>
    //  line 5: <Land_Model_Name>EI_GONE.ALO</Land_Model_Name>
    //  line 6: <Land_Model_Name>Not a model</Land_Model_Name>
    //  line 7: </Unit>
    //  line 8: </Units>
    private const string Xml = """
                               <Units>
                               <Unit Name="A">
                               <Land_Model_Name>EI_TROOPER.ALO</Land_Model_Name>
                               <Land_Terrain_Model_Mapping>Temperate, EI_SNOW.ALO, Desert, EI_SAND.ALO</Land_Terrain_Model_Mapping>
                               <Max_Speed>1.0</Max_Speed>
                               <Land_Model_Name>EI_GONE.ALO</Land_Model_Name>
                               <Land_Model_Name>Not a model</Land_Model_Name>
                               </Unit>
                               </Units>
                               """;

    private static readonly string MappingLine = Xml.Split('\n')[3].TrimEnd('\r');

    private static PreviewModelCodeActionProvider BuildProvider(bool modelPreview = true, string xml = Xml)
    {
        var host = new OneDocumentHost(Uri, xml);
        var fileHelper = new FileHelper(new MockFileSystem());
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Tools = new ToolsFeatureFlags { ModelPreview = modelPreview } });
        var index = GameIndex.Empty with
        {
            AssetFiles = MergedAssetFileIndex.Merge([],
                ["data/art/models/ei_trooper.alo", "data/art/models/ei_snow.alo", "data/art/models/ei_sand.alo"])
        };
        return new PreviewModelCodeActionProvider(TestParseCache.For(host, fileHelper), new ModelTagSchema(),
            new FixedIndex(index), fileHelper, config);
    }

    private static List<CodeAction> At(int line, int character, bool modelPreview = true)
    {
        return BuildProvider(modelPreview).Handle(DocumentUri.From(Uri), new Position(line, character))
            .Select(a => a.CodeAction!)
            .ToList();
    }

    [Fact]
    public void OnAModelItem_OfAMultiLineValue_OffersThatItem()
    {
        // The shipped data writes the mapping one pair per line, so most model items sit on a line
        // where no element starts. Looking only at the element that starts on the cursor line found
        // nothing there, and the editor showed no lightbulb at all.
        const string multiLine = """
                                 <Units>
                                 <Unit Name="A">
                                 <Land_Terrain_Model_Mapping>
                                     Temperate, EI_TROOPER.ALO,
                                     Arctic, EI_SNOW.ALO
                                 </Land_Terrain_Model_Mapping>
                                 </Unit>
                                 </Units>
                                 """;
        var lines = multiLine.Split('\n');

        CodeAction? ActionOn(int line, string model)
        {
            var character = lines[line].IndexOf(model, StringComparison.Ordinal) + 2;
            return BuildProvider(xml: multiLine).Handle(DocumentUri.From(Uri), new Position(line, character))
                .Select(a => a.CodeAction).SingleOrDefault();
        }

        Assert.Equal("Preview model EI_TROOPER.ALO", ActionOn(3, "EI_TROOPER")?.Title);
        Assert.Equal("Preview model EI_SNOW.ALO", ActionOn(4, "EI_SNOW")?.Title);
        Assert.Null(ActionOn(4, "Arctic"));
    }

    [Fact]
    public void OnAModelTag_OffersToPreviewItsModel_FromAnywhereOnTheLine()
    {
        foreach (var character in new[] { 0, 5, 20 })
        {
            var action = Assert.Single(At(2, character));

            Assert.Equal("Preview model EI_TROOPER.ALO", action.Title);
            Assert.Equal(PreviewModelCodeActionProvider.PreviewModelCommand, action.Command!.Name);
            Assert.Equal("EI_TROOPER.ALO", action.Command.Arguments![0].ToString());
            Assert.Null(action.Disabled);
        }
    }

    [Fact]
    public void OnAModelItem_OffersThatItem()
    {
        var snow = Assert.Single(At(3, MappingLine.IndexOf("EI_SNOW", StringComparison.Ordinal) + 2));
        var sand = Assert.Single(At(3, MappingLine.IndexOf("EI_SAND", StringComparison.Ordinal) + 2));

        Assert.Equal("EI_SNOW.ALO", snow.Command!.Arguments![0].ToString());
        Assert.Equal("EI_SAND.ALO", sand.Command!.Arguments![0].ToString());
    }

    [Fact]
    public void OnATerrainItem_OrBetweenItems_OffersNothing()
    {
        Assert.Empty(At(3, MappingLine.IndexOf("Temperate", StringComparison.Ordinal) + 2));
        Assert.Empty(At(3, MappingLine.IndexOf(',', StringComparison.Ordinal)));
    }

    [Fact]
    public void OnATagThatIsNotAModel_OffersNothing()
    {
        Assert.Empty(At(4, 3));
    }

    [Fact]
    public void AModelThatIsNotFound_IsOfferedDisabled_AndSaysWhy()
    {
        var action = Assert.Single(At(5, 3));

        Assert.Equal("Preview model EI_GONE.ALO", action.Title);
        Assert.Equal("Model file not found", action.Disabled?.Reason);
    }

    [Fact]
    public void AValueThatIsNotAnAlo_OffersNothing()
    {
        // The format diagnostic already says what is wrong, and there is nothing to open.
        Assert.Empty(At(6, 3));
    }

    [Fact]
    public void WithModelPreviewOff_OffersNothing()
    {
        Assert.Empty(At(2, 3, false));
    }
}

file sealed class ModelTagSchema : ISchemaProvider
{
    private static readonly XmlTagDefinition[] Tags =
    [
        new()
        {
            Tag = "Land_Model_Name", ValueType = XmlValueType.NameReference, ReferenceKind = ReferenceKind.ModelFile
        },
        new() { Tag = "Max_Speed", ValueType = XmlValueType.Float },
        new()
        {
            Tag = "Land_Terrain_Model_Mapping", ValueType = XmlValueType.TupleList,
            Slots =
            [
                new TupleSlotDefinition { Label = "Terrain", ReferenceKind = ReferenceKind.Enum },
                new TupleSlotDefinition { Label = "Model", ReferenceKind = ReferenceKind.ModelFile }
            ]
        }
    ];

    public XmlTagDefinition? GetTag(string tagName)
    {
        return Tags.FirstOrDefault(t => t.Tag.Equals(tagName, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string _)
    {
        return Tags;
    }

    public GameObjectTypeDefinition? GetObjectType(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string _)
    {
        return Tags;
    }

    public EnumDefinition? GetEnum(string _)
    {
        return null;
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => Tags;
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

file sealed class OneDocumentHost(string uri, string text) : IGameWorkspaceHost
{
    public IEnumerable<TrackedDocument> All => [new(uri, text, 1)];

    public void AddOrUpdate(string u, string t, int version, bool publishDiagnostics = true)
    {
    }

    public void Remove(string u)
    {
    }

    public bool TryGet(string u, out TrackedDocument doc)
    {
        doc = new TrackedDocument(uri, text, 1);
        return true;
    }
}

file sealed class FixedIndex(GameIndex index) : IGameIndexService
{
    public GameIndex Current => index;

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

    public void ApplyLocalisation(ILocalisationIndex localisation)
    {
    }

    public void ApplyAssetFiles(IAssetFileIndex assets)
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
        return new MemoryStream();
    }

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
}