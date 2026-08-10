// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Localisation;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Encyclopedia;

namespace PG.StarWarsGame.LSP.Server.Tests.Encyclopedia;

/// <summary>
///     The four text rules behind the in-game encyclopedia popup: name, class line, body (with the
///     multiplayer override) and the second hop that turns Good_Against/Vulnerable_To object ids
///     into display names.
/// </summary>
public sealed class GetEncyclopediaEntryHandlerTest
{
    private static GameSymbol Sym(string id, string? variantBaseId = null)
    {
        return new GameSymbol(id, GameSymbolKind.XmlObject, "SpaceUnit",
            new FileOrigin($"file:///{id}.xml", 0, 0), null, variantBaseId);
    }

    private static GameIndex IndexWith(FakeLocalisation loca, params GameSymbol[] symbols)
    {
        var defs = symbols.ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s),
            StringComparer.OrdinalIgnoreCase);
        return GameIndex.Empty with { WorkspaceDefinitions = defs, Localisation = loca };
    }

    private static GetEncyclopediaEntryHandler Handler(
        GameIndex index, FakeVariantTagSource source, ILspConfigurationProvider? config = null)
    {
        return new GetEncyclopediaEntryHandler(new FakeGameIndexService(index), new NullSchemaProvider(), source,
            config ?? new FakeLspConfigurationProvider());
    }

    private static VariantTag Tag(string name, string value)
    {
        return new VariantTag(name, value, $"<{name}>{value}</{name}>", 0);
    }

    // ── name, class and body ─────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ResolvesNameClassAndBodyLinesInOrder()
    {
        var loca = new FakeLocalisation()
            .With("TEXT_ETAHN", "Etahn A'baht")
            .With("TEXT_GENERAL", "General")
            .With("TEXT_BIO_1", "=====BIOGRAPHY=====")
            .With("TEXT_BIO_2", "Etahn A'baht is a Dornean General.");
        var index = IndexWith(loca, Sym("Etahn"));
        var source = new FakeVariantTagSource().With("Etahn",
            Tag("Text_ID", "TEXT_ETAHN"),
            Tag("Encyclopedia_Unit_Class", "TEXT_GENERAL"),
            Tag("Encyclopedia_Text", "TEXT_BIO_1, TEXT_BIO_2"));

        var result = await Handler(index, source)
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "Etahn" }, CancellationToken.None);

        Assert.True(result.Found);
        Assert.Equal("Etahn A'baht", result.DisplayName);
        Assert.Equal("General", result.UnitClass);
        Assert.Equal(["=====BIOGRAPHY=====", "Etahn A'baht is a Dornean General."],
            result.Body.Select(l => l.Text));
        Assert.Equal(["TEXT_BIO_1", "TEXT_BIO_2"], result.Body.Select(l => l.Key));
        Assert.False(result.UsedMultiplayerBody);
    }

    [Fact]
    public async Task Handle_UnresolvedKey_KeepsLineWithNullText()
    {
        // A missing translation must stay visible as its key rather than collapsing the line away -
        // a silently shorter popup hides exactly the authoring mistake this preview exists to show.
        var loca = new FakeLocalisation().With("TEXT_OK", "fine");
        var index = IndexWith(loca, Sym("U"));
        var source = new FakeVariantTagSource().With("U", Tag("Encyclopedia_Text", "TEXT_OK, TEXT_MISSING"));

        var result = await Handler(index, source)
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "U" }, CancellationToken.None);

        Assert.Equal(2, result.Body.Count);
        Assert.Equal("fine", result.Body[0].Text);
        Assert.Null(result.Body[1].Text);
        Assert.Equal("TEXT_MISSING", result.Body[1].Key);
    }

    // ── the multiplayer override ─────────────────────────────────────────────

    [Fact]
    public async Task Handle_MultiplayerWithNonEmptyMpBody_ReplacesBody()
    {
        var loca = new FakeLocalisation().With("SP", "single").With("MP", "multi");
        var index = IndexWith(loca, Sym("U"));
        var source = new FakeVariantTagSource().With("U",
            Tag("Encyclopedia_Text", "SP"), Tag("MP_Encyclopedia_Text", "MP"));

        var result = await Handler(index, source).Handle(
            new GetEncyclopediaEntryParams { ObjectId = "U", Multiplayer = true }, CancellationToken.None);

        Assert.Equal(["multi"], result.Body.Select(l => l.Text));
        Assert.True(result.UsedMultiplayerBody);
    }

    [Fact]
    public async Task Handle_MultiplayerWithEmptyMpBody_FallsBackToSinglePlayerBody()
    {
        // The rule is "override only when non-empty" - an empty MP tag is not an instruction to
        // show an empty popup.
        var loca = new FakeLocalisation().With("SP", "single");
        var index = IndexWith(loca, Sym("U"));
        var source = new FakeVariantTagSource().With("U",
            Tag("Encyclopedia_Text", "SP"), Tag("MP_Encyclopedia_Text", "   "));

        var result = await Handler(index, source).Handle(
            new GetEncyclopediaEntryParams { ObjectId = "U", Multiplayer = true }, CancellationToken.None);

        Assert.Equal(["single"], result.Body.Select(l => l.Text));
        Assert.False(result.UsedMultiplayerBody);
    }

    [Fact]
    public async Task Handle_SinglePlayerRequested_IgnoresMpBody()
    {
        var loca = new FakeLocalisation().With("SP", "single").With("MP", "multi");
        var index = IndexWith(loca, Sym("U"));
        var source = new FakeVariantTagSource().With("U",
            Tag("Encyclopedia_Text", "SP"), Tag("MP_Encyclopedia_Text", "MP"));

        var result = await Handler(index, source)
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "U" }, CancellationToken.None);

        Assert.Equal(["single"], result.Body.Select(l => l.Text));
        Assert.False(result.UsedMultiplayerBody);
    }

    // ── variant inheritance ──────────────────────────────────────────────────

    [Fact]
    public async Task Handle_Variant_InheritsEncyclopediaTextFromBase()
    {
        // Encyclopedia tags are routinely left to the base type; resolving against the raw node
        // would preview every variant as blank.
        var loca = new FakeLocalisation().With("BASE_BODY", "inherited body").With("V_NAME", "Variant");
        var index = IndexWith(loca, Sym("V", "B"), Sym("B"));
        var source = new FakeVariantTagSource()
            .With("B", Tag("Encyclopedia_Text", "BASE_BODY"))
            .With("V", Tag("Text_ID", "V_NAME"));

        var result = await Handler(index, source)
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "V" }, CancellationToken.None);

        Assert.Equal("Variant", result.DisplayName);
        Assert.Equal(["inherited body"], result.Body.Select(l => l.Text));
    }

    // ── the second hop ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_GoodAgainstAndVulnerableTo_ResolveTargetDisplayNames()
    {
        var loca = new FakeLocalisation().With("TEXT_TIE", "TIE Fighter").With("TEXT_ISD", "Star Destroyer");
        var index = IndexWith(loca, Sym("U"), Sym("Tie"), Sym("Isd"));
        var source = new FakeVariantTagSource()
            .With("U", Tag("Encyclopedia_Good_Against", "Tie"),
                Tag("Encyclopedia_Vulnerable_To", "Isd"))
            .With("Tie", Tag("Text_ID", "TEXT_TIE"))
            .With("Isd", Tag("Text_ID", "TEXT_ISD"));

        var result = await Handler(index, source)
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "U" }, CancellationToken.None);

        Assert.Equal(["Tie"], result.GoodAgainst.Select(r => r.ObjectId));
        Assert.Equal(["TIE Fighter"], result.GoodAgainst.Select(r => r.DisplayName));
        Assert.Equal(["Star Destroyer"], result.VulnerableTo.Select(r => r.DisplayName));
    }

    // ── active abilities ─────────────────────────────────────────────────────

    private const string ThreeAbilities = """
        <Unit_Abilities_Data SubObjectList="Yes">
            <!-- Primary ability -->
            <Unit_Ability>
                <Type>DEFEND</Type>
                <Recharge_Seconds>30.0f</Recharge_Seconds>
                <GUI_Activated_Ability_Name>Infiltrator_Grenade_Attack</GUI_Activated_Ability_Name>
            </Unit_Ability>
            <Unit_Ability>
                <Type>POWER_TO_WEAPONS</Type>
            </Unit_Ability>
            <Unit_Ability>
                <Type>SPREAD_OUT</Type>
                <Mod_Multiplier>SPEED_MULTIPLIER, 0.5f</Mod_Multiplier>
            </Unit_Ability>
        </Unit_Abilities_Data>
        """;

    [Fact]
    public async Task Handle_UnitAbilities_ReportsEveryAbilityInDocumentOrder()
    {
        // Order is the whole contract: the card slots ability 0 left and ability 1 right. The rest
        // are returned too - the game hides them, and modders rely on that, so whether to draw
        // them is the client's call and not something to decide by truncating here.
        var index = IndexWith(new FakeLocalisation(), Sym("U"));
        var source = new FakeVariantTagSource().With("U",
            new VariantTag("Unit_Abilities_Data", string.Empty, ThreeAbilities, 0));

        var result = await Handler(index, source)
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "U" }, CancellationToken.None);

        Assert.Equal(["DEFEND", "POWER_TO_WEAPONS", "SPREAD_OUT"],
            result.Abilities.Select(a => a.Type));
    }

    [Fact]
    public async Task Handle_UnitAbilities_CapturesTheGuiActivatedNameWhereThereIsOne()
    {
        var index = IndexWith(new FakeLocalisation(), Sym("U"));
        var source = new FakeVariantTagSource().With("U",
            new VariantTag("Unit_Abilities_Data", string.Empty, ThreeAbilities, 0));

        var result = await Handler(index, source)
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "U" }, CancellationToken.None);

        Assert.Equal("Infiltrator_Grenade_Attack", result.Abilities[0].AbilityName);
        Assert.Null(result.Abilities[1].AbilityName);
    }

    [Fact]
    public async Task Handle_NoAbilitiesTag_ReportsNone()
    {
        var index = IndexWith(new FakeLocalisation(), Sym("U"));
        var source = new FakeVariantTagSource().With("U", Tag("Mass", "5"));

        var result = await Handler(index, source)
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "U" }, CancellationToken.None);

        Assert.Empty(result.Abilities);
    }

    [Fact]
    public async Task Handle_AbilityWithoutAType_IsSkippedRatherThanSlottedBlank()
    {
        // Type is what the slot draws until real icons exist, so an entry without one has nothing
        // to show - and silently occupying a slot would shift the ability after it.
        var index = IndexWith(new FakeLocalisation(), Sym("U"));
        var source = new FakeVariantTagSource().With("U", new VariantTag(
            "Unit_Abilities_Data", string.Empty,
            "<Unit_Abilities_Data><Unit_Ability><Recharge_Seconds>1</Recharge_Seconds></Unit_Ability>"
            + "<Unit_Ability><Type>DEFEND</Type></Unit_Ability></Unit_Abilities_Data>",
            0));

        var result = await Handler(index, source)
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "U" }, CancellationToken.None);

        Assert.Equal(["DEFEND"], result.Abilities.Select(a => a.Type));
    }

    [Fact]
    public async Task Handle_Variant_InheritsAbilitiesFromBase()
    {
        var index = IndexWith(new FakeLocalisation(), Sym("V", "B"), Sym("B"));
        var source = new FakeVariantTagSource()
            .With("B", new VariantTag("Unit_Abilities_Data", string.Empty, ThreeAbilities, 0))
            .With("V", Tag("Mass", "1"));

        var result = await Handler(index, source)
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "V" }, CancellationToken.None);

        Assert.Equal(3, result.Abilities.Count);
        Assert.Equal("DEFEND", result.Abilities[0].Type);
    }

    [Fact]
    public async Task Handle_AbilityWithAlternateIcon_ReportsIt()
    {
        // The alternate-state icon is the only icon an ability carries in data - the default is
        // hardcoded in the engine - so it is worth surfacing even though nothing draws it yet.
        var index = IndexWith(new FakeLocalisation(), Sym("U"));
        var source = new FakeVariantTagSource().With("U", new VariantTag(
            "Unit_Abilities_Data", string.Empty,
            "<Unit_Abilities_Data><Unit_Ability><Type>SHIELD</Type>"
            + "<Alternate_Icon_Name>i_shield.tga</Alternate_Icon_Name></Unit_Ability></Unit_Abilities_Data>",
            0));

        var result = await Handler(index, source)
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "U" }, CancellationToken.None);

        Assert.Equal("i_shield.tga", result.Abilities[0].AlternateIconName);
    }

    // ── the population blip ──────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ObjectWithPopulationValue_ReportsIt()
    {
        // The yellow blip at the popup's top-left. Only the galactic card draws it, so it is
        // optional - but it comes from the object either way.
        var index = IndexWith(new FakeLocalisation(), Sym("U"));
        var source = new FakeVariantTagSource().With("U", Tag("Population_Value", "3"));

        var result = await Handler(index, source)
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "U" }, CancellationToken.None);

        Assert.Equal(3, result.PopulationValue);
    }

    [Fact]
    public async Task Handle_ObjectWithoutPopulationValue_ReportsNull()
    {
        var index = IndexWith(new FakeLocalisation(), Sym("U"));
        var source = new FakeVariantTagSource().With("U", Tag("Mass", "5"));

        var result = await Handler(index, source)
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "U" }, CancellationToken.None);

        Assert.Null(result.PopulationValue);
    }

    [Fact]
    public async Task Handle_PopulationValueNotAnInteger_ReportsNullRatherThanGuessing()
    {
        var index = IndexWith(new FakeLocalisation(), Sym("U"));
        var source = new FakeVariantTagSource().With("U", Tag("Population_Value", "lots"));

        var result = await Handler(index, source)
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "U" }, CancellationToken.None);

        Assert.Null(result.PopulationValue);
    }

    [Fact]
    public async Task Handle_Variant_InheritsPopulationValueFromBase()
    {
        var index = IndexWith(new FakeLocalisation(), Sym("V", "B"), Sym("B"));
        var source = new FakeVariantTagSource()
            .With("B", Tag("Population_Value", "2"))
            .With("V", Tag("Mass", "1"));

        var result = await Handler(index, source)
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "V" }, CancellationToken.None);

        Assert.Equal(2, result.PopulationValue);
    }

    // ── layout travels with the text ─────────────────────────────────────────

    [Fact]
    public async Task Handle_CarriesLayoutAlongsideTheText()
    {
        // Layout and text ship together so the panel can never render one against a stale copy of
        // the other.
        var loca = new FakeLocalisation().With("SP", "single");
        var index = IndexWith(loca, Sym("U"));
        var source = new FakeVariantTagSource().With("U", Tag("Encyclopedia_Text", "SP"));

        var result = await Handler(index, source)
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "U" }, CancellationToken.None);

        Assert.Equal(262d, result.Layout.Width);
        Assert.Equal("Arial", result.Layout.Body.FontName);
    }

    // ── absent object and feature flag ───────────────────────────────────────

    [Fact]
    public async Task Handle_UnknownId_NotFound()
    {
        var result = await Handler(IndexWith(new FakeLocalisation()), new FakeVariantTagSource())
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "NOPE" }, CancellationToken.None);

        Assert.False(result.Found);
        Assert.Empty(result.Body);
    }

    [Fact]
    public async Task Handle_EncyclopediaFlagOff_NotFoundDespiteResolvableObject()
    {
        var loca = new FakeLocalisation().With("SP", "single");
        var index = IndexWith(loca, Sym("U"));
        var source = new FakeVariantTagSource().With("U", Tag("Encyclopedia_Text", "SP"));
        var config = FakeLspConfigurationProvider.WithFeatures(
            new FeatureFlags { Tools = new ToolsFeatureFlags { Encyclopedia = false } });

        var result = await Handler(index, source, config)
            .Handle(new GetEncyclopediaEntryParams { ObjectId = "U" }, CancellationToken.None);

        Assert.False(result.Found);
        Assert.Empty(result.Body);
    }

    private sealed class FakeLocalisation : ILocalisationIndex
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public IEnumerable<string> Keys => _values.Keys;

        public bool ContainsKey(string key)
        {
            return _values.ContainsKey(key);
        }

        public string? GetValue(string key)
        {
            return _values.GetValueOrDefault(key);
        }

        public FakeLocalisation With(string key, string value)
        {
            _values[key] = value;
            return this;
        }
    }
}
