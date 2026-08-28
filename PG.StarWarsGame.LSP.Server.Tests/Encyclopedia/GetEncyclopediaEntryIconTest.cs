// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Assets.Icons;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Project;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Encyclopedia;
using PG.StarWarsGame.LSP.Server.Icons;

namespace PG.StarWarsGame.LSP.Server.Tests.Encyclopedia;

/// <summary>
///     The icon half of the popup: whether an object's <c>Icon_Name</c> reaches the card, and how the
///     handler behaves when it cannot be resolved.
/// </summary>
public sealed class GetEncyclopediaEntryIconTest
{
    private const string ObjectId = "TEST_UNIT";

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    private sealed class StubIconCatalogProvider(IconCatalog catalog) : IIconCatalogProvider
    {
        public Task<IconCatalog> GetAsync(string projectRoot, IconProjectSettings? settings, CancellationToken ct)
            => Task.FromResult(catalog);

        public IReadOnlySet<string> IconsAwaitingRepack => catalog.IconsAwaitingRepack;

        public void Invalidate() { }
    }

    private sealed class ThrowingIconCatalogProvider : IIconCatalogProvider
    {
        public Task<IconCatalog> GetAsync(string projectRoot, IconProjectSettings? settings, CancellationToken ct)
            => throw new IOException("atlas unreadable");

        public IReadOnlySet<string> IconsAwaitingRepack => new HashSet<string>();

        public void Invalidate() { }
    }

    private static GameIndex IndexWith(params GameSymbol[] symbols)
    {
        var defs = symbols.ToImmutableDictionary(s => s.Id, s => ImmutableArray.Create(s),
            StringComparer.OrdinalIgnoreCase);
        return GameIndex.Empty with { WorkspaceDefinitions = defs };
    }

    private static GameSymbol Sym() =>
        new(ObjectId, GameSymbolKind.XmlObject, "SpaceUnit", new FileOrigin("file:///u.xml", 0, 0), null, null);

    private static VariantTag Tag(string name, string value) =>
        new(name, value, $"<{name}>{value}</{name}>", 0);

    private static IconCatalog CatalogWith(params string[] baselineNames)
    {
        return new IconCatalog(
            null,
            new Dictionary<string, byte[]>(),
            baselineNames.ToDictionary(n => n, _ => Png, StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<GetEncyclopediaEntryResult> Run(
        IIconCatalogProvider? icons, params VariantTag[] tags)
    {
        var source = new FakeVariantTagSource().With(ObjectId, tags);

        var config = new FakeLspConfigurationProvider();
        config.Current = config.Current with { WorkspaceRoot = @"C:\mod" };

        var handler = new GetEncyclopediaEntryHandler(
            new FakeGameIndexService(IndexWith(Sym())), new NullSchemaProvider(), source, config,
            new WorkspaceIconCatalog(config, icons));

        return await handler.Handle(new GetEncyclopediaEntryParams { ObjectId = ObjectId }, default);
    }

    [Fact]
    public async Task Handle_ResolvedIcon_IsReturnedAsADataUri()
    {
        var result = await Run(
            new StubIconCatalogProvider(CatalogWith("I_BUTTON_TEST.TGA")),
            Tag("Encyclopedia_Text", "TEXT_A"),
            Tag("Icon_Name", "I_BUTTON_TEST.TGA"));

        Assert.NotNull(result.Icon);
        Assert.Equal("I_BUTTON_TEST.TGA", result.Icon.Name);
        Assert.StartsWith("data:image/png;base64,", result.Icon.DataUri);
        Assert.Equal(Convert.ToBase64String(Png), result.Icon.DataUri["data:image/png;base64,".Length..]);
        Assert.Equal(nameof(IconSource.Baseline), result.Icon.Source);
        Assert.False(result.Icon.IsMegaTextureStale);
    }

    // ── ability icons ────────────────────────────────────────────────────────

    private static VariantTag Abilities(params string[] types)
    {
        var entries = string.Join("", types.Select(t => $"<Unit_Ability><Type>{t}</Type></Unit_Ability>"));
        const string name = "Unit_Abilities_Data";
        return new VariantTag(name, "  ", $"<{name}>{entries}</{name}>", 0);
    }

    private static VariantTag AbilityWithAlternateIcon(string type, string alternateIcon)
    {
        const string name = "Unit_Abilities_Data";
        var entry = $"<Unit_Ability><Type>{type}</Type>" +
                    $"<Alternate_Icon_Name>{alternateIcon}</Alternate_Icon_Name></Unit_Ability>";
        return new VariantTag(name, "  ", $"<{name}>{entry}</{name}>", 0);
    }

    /// <summary>
    ///     The <c>Alternate_*</c> family replaces the default it shadows rather than adding a second
    ///     state, so a declared icon wins outright and the type-name guess never runs.
    /// </summary>
    [Fact]
    public async Task Handle_AlternateIconName_ReplacesTheDefaultIcon()
    {
        var catalog = new IconCatalog(
            null,
            new Dictionary<string, byte[]>(),
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["I_SA_DRAIN_LIFE.TGA"] = [7],
                ["I_SA_MINOR_DRAIN_LIFE.TGA"] = [8]
            });

        var result = await Run(
            new StubIconCatalogProvider(catalog),
            Tag("Encyclopedia_Text", "TEXT_A"),
            AbilityWithAlternateIcon("DRAIN_LIFE", "i_sa_minor_drain_life.tga"));

        var ability = Assert.Single(result.Abilities);
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String([8]), ability.Icon?.DataUri);
    }

    /// <summary>
    ///     A declared override that does not resolve draws the missing-icon placeholder, because
    ///     that is exactly what the engine draws - scaled into the ability slot. Falling back to the
    ///     default would be worse than useless: it would show an icon the game never would.
    /// </summary>
    [Fact]
    public async Task Handle_UnresolvableAlternateIcon_ShowsThePlaceholderTheEngineWould()
    {
        var result = await Run(
            new StubIconCatalogProvider(CatalogWith("I_SA_DRAIN_LIFE.TGA")),
            Tag("Encyclopedia_Text", "TEXT_A"),
            AbilityWithAlternateIcon("DRAIN_LIFE", "i_sa_does_not_exist.tga"));

        var ability = Assert.Single(result.Abilities);
        Assert.Equal(
            "data:image/png;base64," + Convert.ToBase64String(FallbackIcon.Png),
            ability.Icon?.DataUri);
    }

    /// <summary>
    ///     A miss on the <c>I_SA_&lt;TYPE&gt;</c> guess draws neither an icon nor the placeholder,
    ///     because it is ambiguous. The engine resolves its hardcoded name from the same mega
    ///     texture, so a stripped entry really does make it draw MISSING - but our guess may simply
    ///     be the wrong name while the game shows fine art. Unable to tell those apart, the slot
    ///     asserts neither and shows the ability type as text.
    /// </summary>
    [Fact]
    public async Task Handle_HeuristicMiss_AssertsNeitherIconNorPlaceholder()
    {
        var result = await Run(
            new StubIconCatalogProvider(CatalogWith("I_SA_BERSERKER.TGA")),
            Tag("Encyclopedia_Text", "TEXT_A"),
            Abilities("BARRAGE"));

        var ability = Assert.Single(result.Abilities);
        Assert.Null(ability.Icon);
    }

    /// <summary>
    ///     Ability icons are found by guessing <c>I_SA_&lt;TYPE&gt;</c>: nothing in the game's data
    ///     links an ability to its icon, so the engine's mapping cannot be read.
    /// </summary>
    [Fact]
    public async Task Handle_AbilityIcon_IsFoundByTypeName()
    {
        var result = await Run(
            new StubIconCatalogProvider(CatalogWith("I_SA_BERSERKER.TGA")),
            Tag("Encyclopedia_Text", "TEXT_A"),
            Abilities("BERSERKER"));

        var ability = Assert.Single(result.Abilities);
        Assert.Equal("BERSERKER", ability.Type);
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(Png), ability.Icon?.DataUri);
    }

    /// <summary>
    ///     The property that makes a guess acceptable: the catalog validates it, so an ability whose
    ///     icon is named differently gets NO icon rather than someone else's.
    /// </summary>
    [Fact]
    public async Task Handle_AbilityIconThatDoesNotExist_YieldsNoIconRatherThanAWrongOne()
    {
        var result = await Run(
            new StubIconCatalogProvider(CatalogWith("I_SA_BERSERKER.TGA")),
            Tag("Encyclopedia_Text", "TEXT_A"),
            Abilities("BARRAGE"));

        var ability = Assert.Single(result.Abilities);
        Assert.Equal("BARRAGE", ability.Type);
        Assert.Null(ability.Icon);
    }

    [Fact]
    public async Task Handle_AbilityIcons_ResolveIndependentlyPerAbility()
    {
        var result = await Run(
            new StubIconCatalogProvider(CatalogWith("I_SA_BERSERKER.TGA")),
            Tag("Encyclopedia_Text", "TEXT_A"),
            Abilities("BARRAGE", "BERSERKER"));

        Assert.Collection(result.Abilities,
            first => Assert.Null(first.Icon),
            second => Assert.NotNull(second.Icon));
    }

    [Fact]
    public async Task Handle_WithoutIconServices_AbilitiesStillListedWithoutIcons()
    {
        var result = await Run(null, Tag("Encyclopedia_Text", "TEXT_A"), Abilities("BERSERKER"));

        var ability = Assert.Single(result.Abilities);
        Assert.Null(ability.Icon);
    }

    [Fact]
    public async Task Handle_ObjectWithoutIconName_ReturnsNoIcon()
    {
        var result = await Run(
            new StubIconCatalogProvider(CatalogWith("I_BUTTON_TEST.TGA")),
            Tag("Encyclopedia_Text", "TEXT_A"));

        Assert.Null(result.Icon);
    }

    // The object names an icon, so the card keeps its portrait slot and says the art is missing -
    // rather than collapsing to a card that looks like the object never had an icon.
    [Fact]
    public async Task Handle_IconNameThatResolvesToNothing_FallsBackToThePlaceholder()
    {
        var result = await Run(
            new StubIconCatalogProvider(CatalogWith()),
            Tag("Encyclopedia_Text", "TEXT_A"),
            Tag("Icon_Name", "I_BUTTON_MISSING.TGA"));

        Assert.NotNull(result.Icon);
        Assert.Equal("I_BUTTON_MISSING.TGA", result.Icon.Name);
        Assert.Equal("Fallback", result.Icon.Source);
        Assert.False(result.Icon.IsMegaTextureStale);
        Assert.Equal(
            "data:image/png;base64," + Convert.ToBase64String(FallbackIcon.Png),
            result.Icon.DataUri);
    }

    // Icon services are optional, so a server built without them still answers with text.
    [Fact]
    public async Task Handle_WithoutIconServices_StillReturnsTheEntry()
    {
        var result = await Run(null, Tag("Encyclopedia_Text", "TEXT_A"), Tag("Icon_Name", "I_X.TGA"));

        Assert.True(result.Found);
        Assert.Null(result.Icon);
    }

    // The card is perfectly readable without artwork; a broken atlas must not cost the caller the text.
    [Fact]
    public async Task Handle_IconLookupThrows_StillReturnsTheEntry()
    {
        var result = await Run(
            new ThrowingIconCatalogProvider(),
            Tag("Encyclopedia_Text", "TEXT_A"),
            Tag("Icon_Name", "I_BUTTON_TEST.TGA"));

        Assert.True(result.Found);
        Assert.Null(result.Icon);
    }

    // ── chrome ───────────────────────────────────────────────────────────────

    // BOTH header bands, not just one. E_TOPBAR carries the population blip's black disc baked into
    // its left end and E_TOPBAR2 is the same 262x20 band without it, so the client picks the variant
    // by whether the object has a Population_Value. Cutting only E_TOPBAR left it stamping a disc on
    // every card, population or not.
    [Fact]
    public async Task Handle_Chrome_CutsBothHeaderBandVariants()
    {
        var result = await Run(
            new StubIconCatalogProvider(CatalogWith("E_TOPBAR.TGA", "E_TOPBAR2.TGA")),
            Tag("Encyclopedia_Text", "TEXT_A"));

        Assert.NotNull(result.Chrome);
        Assert.StartsWith("data:image/png;base64,", result.Chrome.TopBar?.DataUri);
        Assert.StartsWith("data:image/png;base64,", result.Chrome.TopBarNoBlip?.DataUri);
    }

    // Per-piece nulls are normal - a mod may ship an atlas with only some of the chrome.
    [Fact]
    public async Task Handle_ChromePieceAbsent_LeavesThatPieceNull()
    {
        var result = await Run(
            new StubIconCatalogProvider(CatalogWith("E_TOPBAR.TGA")),
            Tag("Encyclopedia_Text", "TEXT_A"));

        Assert.NotNull(result.Chrome);
        Assert.NotNull(result.Chrome.TopBar);
        Assert.Null(result.Chrome.TopBarNoBlip);
    }

    // Nothing resolved at all collapses to a single null, so the client's "do I have chrome?" test
    // stays one check rather than six.
    [Fact]
    public async Task Handle_NoChromeInAtlas_SendsNullRatherThanAnEmptyRecord()
    {
        var result = await Run(
            new StubIconCatalogProvider(CatalogWith("I_BUTTON_TEST.TGA")),
            Tag("Encyclopedia_Text", "TEXT_A"));

        Assert.Null(result.Chrome);
    }

    // ── the backdrop's name is data ──────────────────────────────────────────

    /// <summary>
    ///     Runs the handler with a CommandBarComponent in the index as well as the object, so the
    ///     layout resolver sees a mod's overridden card.
    /// </summary>
    private static async Task<GetEncyclopediaEntryResult> RunWithComponent(
        IIconCatalogProvider? icons, string componentId, VariantTag[] componentTags,
        params VariantTag[] objectTags)
    {
        var component = new GameSymbol(componentId, GameSymbolKind.XmlObject, "CommandBarComponent",
            new FileOrigin("file:///Commandbarcomponents.xml", 0, 0), null, null);

        var source = new FakeVariantTagSource()
            .With(ObjectId, objectTags)
            .With(componentId, componentTags);

        var config = new FakeLspConfigurationProvider();
        config.Current = config.Current with { WorkspaceRoot = @"C:\mod" };

        var handler = new GetEncyclopediaEntryHandler(
            new FakeGameIndexService(IndexWith(Sym(), component)), new NullSchemaProvider(),
            source, config, new WorkspaceIconCatalog(config, icons));

        return await handler.Handle(new GetEncyclopediaEntryParams { ObjectId = ObjectId }, default);
    }

    /// <summary>
    ///     <c>encyclopedia_back</c> names its own backdrop in <c>Blank_Texture_Name</c>, so a reskin
    ///     that renames it must still draw. The other chrome pieces appear in no shipped XML and stay
    ///     hardcoded, which is the correct treatment for names the engine really does fix.
    /// </summary>
    [Fact]
    public async Task Handle_ModRenamesBackdrop_CutsTheTextureTheModNames()
    {
        var result = await RunWithComponent(
            new StubIconCatalogProvider(CatalogWith("MY_BACKDROP.TGA", "E_BACKGROUND.TGA")),
            "encyclopedia_back", [Tag("Blank_Texture_Name", "my_backdrop.tga")],
            Tag("Encyclopedia_Text", "TEXT_A"));

        Assert.NotNull(result.Chrome);
        Assert.NotNull(result.Chrome.Background);
    }

    // ── the faction switch ───────────────────────────────────────────────────

    /// <summary>
    ///     One entry per slot the component lists, in list order, each carrying the name it was cut
    ///     from so a mislabelled slot is still identifiable.
    /// </summary>
    [Fact]
    public async Task Handle_FactionFrames_AreCutForEverySlotInOrder()
    {
        var result = await Run(
            new StubIconCatalogProvider(
                CatalogWith("I_TOOLTIP_REBEL_FRAME.TGA", "I_TOOLTIP_EMPIRE_FRAME.TGA")),
            Tag("Encyclopedia_Text", "TEXT_A"));

        Assert.NotNull(result.Chrome);
        Assert.Collection(result.Chrome.FactionFrames,
            rebel =>
            {
                Assert.Equal(0, rebel.Slot);
                Assert.Equal("i_tooltip_rebel_frame.tga", rebel.TextureName);
                Assert.Equal("Rebel", rebel.SlotName);
                Assert.StartsWith("data:image/png;base64,", rebel.Image?.DataUri);
            },
            empire =>
            {
                Assert.Equal(1, empire.Slot);
                Assert.Equal("i_tooltip_empire_frame.tga", empire.TextureName);
                Assert.Equal("Empire", empire.SlotName);
                Assert.StartsWith("data:image/png;base64,", empire.Image?.DataUri);
            });
    }

    /// <summary>
    ///     A named frame the atlas does not carry keeps its slot with no art. Dropping the slot would
    ///     hide the authoring mistake, and would silently renumber every slot after it - which is the
    ///     one thing an indexed list cannot survive.
    /// </summary>
    [Fact]
    public async Task Handle_FactionFrameMissingFromAtlas_KeepsTheSlotWithoutArt()
    {
        var result = await Run(
            new StubIconCatalogProvider(CatalogWith("I_TOOLTIP_REBEL_FRAME.TGA")),
            Tag("Encyclopedia_Text", "TEXT_A"));

        Assert.NotNull(result.Chrome);
        Assert.Collection(result.Chrome.FactionFrames,
            rebel => Assert.NotNull(rebel.Image),
            empire =>
            {
                Assert.Equal(1, empire.Slot);
                Assert.Equal("i_tooltip_empire_frame.tga", empire.TextureName);
                Assert.Null(empire.Image);
            });
    }

    /// <summary>
    ///     Only the first two slots have names the game's own data confirms - the shipped textures
    ///     say rebel and empire. A mod's third faction could be anyone, so naming it would be a
    ///     guess; the slot travels unnamed and the client falls back to its index.
    /// </summary>
    [Fact]
    public async Task Handle_FactionSlotBeyondTheShippedPair_IsLeftUnnamed()
    {
        var result = await RunWithComponent(
            new StubIconCatalogProvider(CatalogWith("MY_FRAME.TGA")),
            "encyclopedia_back",
            [Tag("Icon_Alternate_Texture_Name", "a.tga b.tga my_frame.tga")],
            Tag("Encyclopedia_Text", "TEXT_A"));

        Assert.NotNull(result.Chrome);
        var third = Assert.Single(result.Chrome.FactionFrames, f => f.Slot == 2);
        Assert.Null(third.SlotName);
        Assert.Equal("my_frame.tga", third.TextureName);
        Assert.NotNull(third.Image);
    }

    /// <summary>
    ///     A faction frame is chrome like any other piece, so finding only one still means there is
    ///     chrome to send.
    /// </summary>
    [Fact]
    public async Task Handle_OnlyAFactionFrameResolves_StillSendsChrome()
    {
        var result = await Run(
            new StubIconCatalogProvider(CatalogWith("I_TOOLTIP_REBEL_FRAME.TGA")),
            Tag("Encyclopedia_Text", "TEXT_A"));

        Assert.NotNull(result.Chrome);
        Assert.Null(result.Chrome.TopBar);
        Assert.NotNull(Assert.Single(result.Chrome.FactionFrames, f => f.Slot == 0).Image);
    }

    /// <summary>
    ///     No frame art anywhere still lists the slots: the switch is driven by what the XML declares,
    ///     not by what the atlas happens to carry.
    /// </summary>
    [Fact]
    public async Task Handle_NoFactionFrameArt_StillListsTheDeclaredSlots()
    {
        var result = await Run(
            new StubIconCatalogProvider(CatalogWith("E_TOPBAR.TGA")),
            Tag("Encyclopedia_Text", "TEXT_A"));

        Assert.NotNull(result.Chrome);
        Assert.Equal(2, result.Chrome.FactionFrames.Count);
        Assert.All(result.Chrome.FactionFrames, f => Assert.Null(f.Image));
    }
}
