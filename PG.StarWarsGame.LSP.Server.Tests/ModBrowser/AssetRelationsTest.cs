// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Assets;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.ModBrowser;

namespace PG.StarWarsGame.LSP.Server.Tests.ModBrowser;

/// <summary>
///     What an asset, a script or an enum opens into (#270). Which objects use an asset needs every
///     object's tags, so it is counted only once asked for and then kept for the index.
/// </summary>
public sealed class AssetRelationsTest
{
    private const string Units = "file:///c:/work/mod/data/xml/units.xml";
    private const string TieScript = "file:///c:/work/mod/data/scripts/gameobject/tiescript.lua";
    private const string PgBase = "file:///c:/work/mod/data/scripts/library/pgbase.lua";

    private static readonly EnumDefinition s_animationType = new() { Name = "AnimationType", Values = [] };

    private static readonly Dictionary<string, XmlTagDefinition> s_tags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space_Model_Name"] = Tag("Space_Model_Name", ReferenceKind.ModelFile),
        ["Icon_Name"] = Tag("Icon_Name", ReferenceKind.TextureFile),
        ["Lua_Script"] = Tag("Lua_Script", ReferenceKind.WorkspaceFile),
        ["Idle_Animation"] = Tag("Idle_Animation", ReferenceKind.Enum) with { Enum = s_animationType }
    };

    private static XmlTagDefinition Tag(string name, ReferenceKind kind)
    {
        return new XmlTagDefinition { Tag = name, ValueType = XmlValueType.NameReference, ReferenceKind = kind };
    }

    private sealed class Schema : NullSchemaProvider
    {
        public override IReadOnlyList<XmlTagDefinition> AllTags => [.. s_tags.Values];

        public override XmlTagDefinition? GetTag(string tagName)
        {
            return s_tags.GetValueOrDefault(tagName);
        }
    }

    private sealed class Tags : IVariantTagSource
    {
        public int Asked { get; private set; }

        public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
        {
            Asked++;
            return objectId == "Tie_Fighter"
                ?
                [
                    new VariantTag("Space_Model_Name", "EV_TIE.alo", "", 0),
                    new VariantTag("Icon_Name", "I_Tie.tga", "", 1),
                    new VariantTag("Lua_Script", "TieScript", "", 2)
                ]
                : null;
        }
    }

    private readonly Tags _tags = new();

    private SymbolRelations Relations()
    {
        var tie = new GameSymbol("Tie_Fighter", GameSymbolKind.XmlObject, "GameObjectType", new FileOrigin(Units, 2, 4),
            null);
        var loads = new GameReference(WorkspaceFileKey.Create(WorkspaceFileKey.LuaScriptType, "TieScript"),
            GameSymbolKind.WorkspaceFile, WorkspaceFileKey.LuaScriptType, Units, 5, 8, 9);
        var index = GameIndex.Empty with
        {
            Documents = ImmutableDictionary<string, DocumentIndex>.Empty
                .Add(Units, new DocumentIndex(Units, 1, [tie], [loads], LayerRank: 0, LayerName: "Mod"))
                .Add(TieScript, new DocumentIndex(TieScript, 1, [], [], ["PGBase"], LayerRank: 0, LayerName: "Mod"))
                .Add(PgBase, new DocumentIndex(PgBase, 1, [], [], LayerRank: 0, LayerName: "Mod")),
            WorkspaceDefinitions =
            ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("Tie_Fighter", [tie]),
            WorkspaceReferences = ImmutableDictionary<string, ImmutableArray<GameReference>>.Empty
                .Add(loads.TargetId, [loads]),
            AssetFiles = MergedAssetFileIndex.Merge(
            [
                "data/art/models/ev_tie.alo", "data/art/models/ev_tie_idle.ala", "data/art/models/ev_tie_die.ala",
                "data/art/models/p_smoke.alo", "data/art/textures/tie.tga", "data/art/textures/smoke.tga",
                "data/art/textures/i_tie.tga"
            ], []),
            ParticleSystems = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "p_smoke.alo"),
            ModelTextures = ImmutableDictionary<string, ImmutableArray<string>>.Empty
                .WithComparers(StringComparer.OrdinalIgnoreCase)
                .Add("ev_tie.alo", ["Tie.tga"]).Add("p_smoke.alo", ["Smoke.tga"])
        };
        // The model carries two effects; one names a particle system no layer ships.
        return new SymbolRelations(index, new Schema(), _tags, SymbolCatalog.Build(index, [], [s_animationType]),
            path => path.EndsWith("ev_tie.alo", StringComparison.OrdinalIgnoreCase)
                ? ["P_Smoke_ALT2", "P_Missing_LOD0"]
                : []);
    }

    private static Dictionary<string, SymbolRelation> RelationsOf(SymbolRelations relations, string page, string id)
    {
        return relations.Node(page, id).Relations.ToDictionary(r => r.Relation);
    }

    private static IEnumerable<string> Names(IReadOnlyList<SymbolRow>? rows)
    {
        return rows!.Select(r => r.Name);
    }

    private List<(string Action, string? Reason)> ActionsOf(string page, string id)
    {
        return Relations().Node(page, id).Actions.Select(a => (a.Action, a.Reason)).ToList();
    }

    [Fact]
    public void AModel_OpensInThePreview_AndHasNoTextToGoTo()
    {
        Assert.Equal([("definition", "binary"), ("previewModel", null)], ActionsOf("models", "ev_tie.alo"));
        Assert.Equal([("definition", "binary"), ("previewModel", null)], ActionsOf("particleSystems", "p_smoke.alo"));
        Assert.Equal([("definition", "binary")], ActionsOf("textures", "tie.tga"));
    }

    [Fact]
    public void ALuaScript_GoesToItsFile()
    {
        var node = Relations().Node("luaScripts", "tiescript.lua");

        Assert.Equal(new SymbolLocation(TieScript, 0), node.Location);
        Assert.Equal([("definition", null)], node.Actions.Select(a => (a.Action, a.Reason)));
    }

    [Fact]
    public void AnEnum_IsTheSchemas_NotAFileToGoTo()
    {
        Assert.Equal([("definition", "schema")], ActionsOf("enums", "AnimationType"));
    }

    [Fact]
    public void AModel_OpensIntoItsAnimationsAndTextures_ItsUsersUncountedUntilAskedFor()
    {
        var relations = Relations();

        var node = RelationsOf(relations, "models", "ev_tie.alo");

        Assert.Equal(["animations", "particleSystems", "textures", "usedBy"], node.Keys);
        Assert.Equal(2, node["animations"].Count);
        Assert.Equal(2, node["particleSystems"].Count);
        Assert.Equal(1, node["textures"].Count);
        Assert.Null(node["usedBy"].Count);
        Assert.Equal(0, _tags.Asked);
        Assert.Equal(["ev_tie_die.ala", "ev_tie_idle.ala"],
            Names(relations.Rows("models", "ev_tie.alo", "animations")));
        Assert.Equal(["tie.tga"], Names(relations.Rows("models", "ev_tie.alo", "textures")));
        Assert.Equal(["Tie_Fighter"], Names(relations.Rows("models", "ev_tie.alo", "usedBy")));
    }

    // A proxy names its particle system with the level tags the engine strips before loading it.
    [Fact]
    public void AModel_OpensIntoTheParticleSystemsItsProxiesName()
    {
        var rows = Relations().Rows("models", "ev_tie.alo", "particleSystems")!;

        Assert.Equal([("p_missing.alo", "missing"), ("p_smoke.alo", "game")], rows.Select(r => (r.Name, r.Origin))
            .OrderBy(r => r.Item1));
    }

    [Fact]
    public void AParticleSystem_OpensIntoItsTextures()
    {
        Assert.Equal(["smoke.tga"], Names(Relations().Rows("particleSystems", "p_smoke.alo", "textures")));
    }

    [Fact]
    public void AnAnimation_OpensIntoItsModel()
    {
        Assert.Equal(["ev_tie.alo"], Names(Relations().Rows("animations", "ev_tie_die.ala", "model")));
    }

    [Fact]
    public void ATexture_IsUsedByTheModelsAndObjectsNamingIt()
    {
        var relations = Relations();

        Assert.Equal(["ev_tie.alo"], Names(relations.Rows("textures", "tie.tga", "usedBy")));
        Assert.Equal(["Tie_Fighter"], Names(relations.Rows("textures", "i_tie.tga", "usedBy")));
    }

    [Fact]
    public void ALuaScript_IsLoadedByTheObjectsNamingIt_AndRequiresItsLibraries()
    {
        var relations = Relations();

        Assert.Equal(["Tie_Fighter"], Names(relations.Rows("luaScripts", "tiescript.lua", "loadedBy")));
        Assert.Equal(["pgbase.lua"], Names(relations.Rows("luaScripts", "tiescript.lua", "requires")));
    }

    [Fact]
    public void AnEnum_IsUsedByTheTagsTypedWithIt()
    {
        Assert.Equal(["Idle_Animation"], Names(Relations().Rows("enums", "AnimationType", "usedBy")));
    }
}