// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.CodeLens;
using PG.StarWarsGame.LSP.Xml.Tests.Fakes;

namespace PG.StarWarsGame.LSP.Xml.Tests.CodeLens;

/// <summary>
///     The "preview unit" lens: the way into the model preview from the object you are editing.
/// </summary>
/// <remarks>
///     Before this, the only route was the command palette plus typing the object's name from
///     memory, and opening the <c>.alo</c> instead gave a scene with no hardpoints, no weapons and
///     no reticles - because only an <c>objectId</c> request assembles those.
/// </remarks>
public sealed class PreviewUnitCodeLensProviderTest
{
    private static PreviewUnitCodeLensProvider Provider(
        IVariantTagSource source, ILspConfigurationProvider? config = null)
    {
        return new PreviewUnitCodeLensProvider(source, config ?? new FakeLspConfigurationProvider());
    }

    private static GameSymbol Sym(string id, string? baseId = null)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "SpaceUnit",
            new FileOrigin($"file:///{id}.xml", 7, 0), null, baseId);
    }

    private static GameIndex IndexWith(params GameSymbol[] symbols)
    {
        var defs = symbols.ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s),
            StringComparer.OrdinalIgnoreCase);
        return GameIndex.Empty with { WorkspaceDefinitions = defs };
    }

    private static CodeLensSymbolContext Ctx(GameSymbol symbol, GameIndex index)
    {
        return new CodeLensSymbolContext(symbol, (FileOrigin)symbol.Origin, index);
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }

    private sealed class TagSource : IVariantTagSource
    {
        private readonly Dictionary<string, IReadOnlyList<VariantTag>> _byId =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<VariantTag>? TryGetTags(string objectId)
        {
            return _byId.GetValueOrDefault(objectId);
        }

        public TagSource With(string id, params VariantTag[] tags)
        {
            _byId[id] = tags;
            return this;
        }
    }

    [Fact]
    public void Handle_ObjectWithATacticalModel_EmitsLensCarryingTheObjectId()
    {
        var symbol = Sym("Generic_Star_Destroyer");
        var source = new TagSource()
            .With("Generic_Star_Destroyer", Tag("Space_Model_Name", "EV_StarDestroyer.ALO"));

        var lens = Provider(source).Handle(Ctx(symbol, IndexWith(symbol)));

        Assert.NotNull(lens);
        Assert.Equal(PreviewUnitCodeLensProvider.PreviewUnitCommand, lens!.Command!.Name);

        // The OBJECT id, never the model file: only an objectId request assembles the hardpoints,
        // weapons and reticles that the Gameplay lens is made of.
        Assert.Contains("Generic_Star_Destroyer", lens.Command.Arguments!.ToString());
        Assert.DoesNotContain("EV_StarDestroyer.ALO", lens.Command.Arguments.ToString());
        Assert.Equal(7, lens.Range.Start.Line);
    }

    [Fact]
    public void Handle_AlwaysReadsPreviewUnit()
    {
        // One word for every case, chosen deliberately: a land unit, a space unit, a structure and
        // a prop all open the same viewer in the same state, and a title that changed with the
        // subject would give a reader something new to interpret on every line.
        var symbol = Sym("U");
        var source = new TagSource().With("U", Tag("Land_Model_Name", "IV_Trooper.ALO"));

        Assert.Equal("preview unit", Provider(source).Handle(Ctx(symbol, IndexWith(symbol)))!
            .Command!.Title);
    }

    [Theory]
    [InlineData("Space_Model_Name")]
    [InlineData("Land_Model_Name")]
    [InlineData("Model_Name")]
    public void Handle_AnyTacticalModelTag_EmitsLens(string tag)
    {
        // The three the engine treats as an object's tactical model. Read from the shared list on
        // HardpointBoneModelResolver rather than restated here, so a fourth would reach both.
        var symbol = Sym("U");
        var source = new TagSource().With("U", Tag(tag, "Whatever.ALO"));

        Assert.NotNull(Provider(source).Handle(Ctx(symbol, IndexWith(symbol))));
    }

    [Fact]
    public void Handle_ObjectWithNoModelAtAll_EmitsNothing()
    {
        // A lens that opened a panel saying "declares no tactical model, so there is nothing to
        // draw" is worse than no lens: it is an invitation to a dead end.
        var symbol = Sym("U");
        var source = new TagSource().With("U", Tag("Max_Health", "100"));

        Assert.Null(Provider(source).Handle(Ctx(symbol, IndexWith(symbol))));
    }

    [Fact]
    public void Handle_ModelToAttachIsNotATacticalModel()
    {
        // A HardPoint declares the model it MOUNTS, which is not a subject of its own - previewing
        // it would draw a turret with no ship under it.
        var symbol = Sym("HP_Star_Destroyer_Weapon_FL");
        var source = new TagSource().With("HP_Star_Destroyer_Weapon_FL",
            Tag("Model_To_Attach", "EV_StarDestroyer_HP00_L-F.alo"));

        Assert.Null(Provider(source).Handle(Ctx(symbol, IndexWith(symbol))));
    }

    [Fact]
    public void Handle_VariantInheritingItsModelFromTheBase_EmitsLens()
    {
        // A variant typically restates only what it changes, and its model comes from the base. The
        // preview assembles through the same chain, so a lens gated on restating it would be missing
        // from most of the objects in a mod.
        var variant = Sym("V", "B");
        var index = IndexWith(variant, Sym("B"));
        var source = new TagSource()
            .With("V", Tag("Max_Health", "100"))
            .With("B", Tag("Space_Model_Name", "EV_StarDestroyer.ALO"));

        Assert.NotNull(Provider(source).Handle(Ctx(variant, index)));
    }

    [Fact]
    public void Handle_CyclicVariantChainWithoutAModel_TerminatesAndEmitsNothing()
    {
        var a = Sym("A", "B");
        var index = IndexWith(a, Sym("B", "A"));
        var source = new TagSource().With("A", Tag("Mass", "5")).With("B", Tag("Mass", "6"));

        Assert.Null(Provider(source).Handle(Ctx(a, index)));
    }

    [Fact]
    public void Handle_ModelPreviewFlagOff_EmitsNothing()
    {
        // The same flag the command's own enablement uses. A lens that fired a disabled command
        // would report "command not found" at the reader.
        var symbol = Sym("U");
        var source = new TagSource().With("U", Tag("Space_Model_Name", "X.ALO"));
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Tools = new ToolsFeatureFlags { ModelPreview = false } });

        Assert.Null(Provider(source, config).Handle(Ctx(symbol, IndexWith(symbol))));
    }

    [Fact]
    public void Handle_SymbolThatIsNotAnXmlObject_EmitsNothing()
    {
        // Assets and localisation keys reach the same registry. Only an XML object can be assembled.
        var asset = new GameSymbol("EV_StarDestroyer.ALO", GameSymbolKind.Asset, null,
            new FileOrigin("file:///a.xml", 1, 0), null);
        var source = new TagSource()
            .With("EV_StarDestroyer.ALO", Tag("Space_Model_Name", "X.ALO"));

        Assert.Null(Provider(source).Handle(Ctx(asset, IndexWith(asset))));
    }
}
