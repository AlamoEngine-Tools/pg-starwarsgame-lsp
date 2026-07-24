// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     E2E smoke tests for XML GoTo Definition on plain object-name references
///     (NameReference/TypeReference tags resolving to workspace-defined XML objects).
///     Regression coverage for the 2026-07-05 report that go-to-definition stopped
///     working in XML files for every Name/Type reference while Lua kept working.
/// </summary>
[Trait("Category", "E2E")]
public sealed class XmlGoToSmokeTest : IClassFixture<EawLspServerFixture>
{
    private const string CorvettesXmlRel = "Data/Xml/Spaceunitscorvettes.xml";
    private const string FightersXmlRel = "Data/Xml/Spaceunitsfighters.xml";
    private const string FactionsXmlRel = "Data/Xml/Factions.xml";
    private const string CampaignsXmlRel = "Data/Xml/Campaigns_Alpha.xml";

    private readonly EawLspServerFixture _fixture;

    public XmlGoToSmokeTest(EawLspServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task XmlGoTo_SfxEventNameReference_ReturnsWorkspaceDefinitionLocation()
    {
        await RunGoToAsync("SFXEvent_Select", "Unit_Select_Tartan", "Sfxeventsunitscorvettes");
    }

    [Fact]
    public async Task XmlGoTo_ProjectileTypeReference_ReturnsWorkspaceDefinitionLocation()
    {
        await RunGoToAsync("Projectile_Types", "Proj_Ship_Diamond_Boron_Missile", "Projectiles");
    }

    [Fact]
    public async Task XmlGoTo_SecondTokenOfCommaSeparatedHardPointList_ReturnsHardpointsDefinition()
    {
        // <HardPoints> spans multiple lines; HP_Corellian_Corvette_02 is the second token of a
        // comma-separated TypeReferenceList whose values sit on their own line.
        await RunGoToAsync(null, "HP_Corellian_Corvette_02", "Hardpoints");
    }

    [Fact]
    public async Task XmlGoTo_SecondTokenOfPipeSeparatedCategoryMask_ReturnsEnumDefinition()
    {
        // <CategoryMask> Corvette | AntiFighter | AntiBomber </CategoryMask> - dynamic-enum list,
        // pipe-separated; the second token must navigate to the workspace enum definition file.
        await RunGoToAsync(null, "AntiFighter", "Gameobjectcategorytype");
    }

    [Fact]
    public async Task XmlGoTo_FrigateFromSpaceSeparatedEncyclopediaList_NeverOpenedTargetFile_Works()
    {
        // Exact user-reported scenario (2026-07-05): from an open Spaceunitsfighters.xml,
        // <Encyclopedia_Good_Against> Calamari_Cruiser Alliance_Assault_Frigate Nebulon_B_Frigate …
        // (space-separated list) must navigate to Spaceunitsfrigates.xml even though that file
        // was never opened in the editor - its symbols come purely from the workspace scan.
        await RunGoToAsync(null, "Nebulon_B_Frigate", "Spaceunitsfrigates", FightersXmlRel);
    }

    [Fact]
    public async Task XmlGoTo_ShadowBlobMaterialReference_ReturnsShadowblobmaterialsDefinition()
    {
        // ShadowBlobMaterial is a first-class object type (2026-07-05):
        // <Reinforcements_Shadow_Blob_Material_Name> Reinforcement_Overlay_Empire </…> in
        // Factions.xml must navigate to the <Material name="…"> entry in Shadowblobmaterials.xml
        // (directContent-registered, lowercase `name` attribute).
        await RunGoToAsync(null, "Reinforcement_Overlay_Empire", "Shadowblobmaterials", FactionsXmlRel);
    }

    [Fact]
    public async Task XmlGoTo_SkirmishDefaultForcesListToken_ReturnsSquadronsDefinition()
    {
        // Regression for #77: <Space_Skirmish_AI_Default_Forces> was declared TypeReferenceList
        // in the schema but carried no referenceKind, so XmlGameDocumentParser emitted no
        // GameReference for its tokens at all - go-to silently did nothing AND no unresolved
        // reference diagnostic appeared. The neighbouring <Skirmish_Land_Bomber> worked because
        // it declares referenceKind: xmlObject, which is what made the two look inconsistent.
        await RunGoToAsync(null, "Rebel_X-Wing_Squadron", "Squadrons", FactionsXmlRel);
    }

    [Fact]
    public async Task XmlGoTo_SfxEventInsideTupleTag_ReturnsSfxEventDefinition()
    {
        // #78: tuple-valued SFX tags (HardPointSfxMap / AbilitySfxMap / ConditionalSfxEvent) get
        // positional completion and pair validation, but XmlGameDocumentParser emits no
        // GameReference for their elements - so go-to on the SFXEvent half silently does nothing
        // while the very same event name navigates fine from a plain SFXEventReference tag.
        // <SFXEvent_Hardpoint_Destroyed> HARD_POINT_WEAPON_LASER, Unit_Lost_Laser_Calamari </…>
        await RunGoToAsync(null, "Unit_Lost_Laser_Calamari", "Sfxeventsunitscapital",
            "Data/Xml/Spaceunitscapital.xml");
    }

    [Fact]
    public async Task XmlGoTo_OwnerAgnosticAbilityName_ResolvesAcrossOwners()
    {
        // Abilities are indexed owner-scoped as {ownerId}$Name, but the galactic ability lists in
        // GameConstants name them bare - the engine accepts any object's ability of that name. The
        // bare name therefore matches no indexed id, and before ownerAgnosticReference these tags
        // had neither go-to nor unresolved-reference validation.
        // <Activated_Slice_Ability_Names> Tani_Slicer,R2D2_Slicer </…> resolves to the
        // <..._Ability Name="R2D2_Slicer"> defined on R2-D2 in Namedherounits.xml.
        await RunGoToAsync(null, "R2D2_Slicer", "Namedherounits", "Data/Xml/Gameconstants.xml");
    }

    [Fact]
    public async Task XmlGoTo_HardpointSpecialAbilityName_ResolvesToTheAbilityDefinition()
    {
        // Special_Ability_Name was typed referenceKind: unknown - deliberately unindexed, because
        // abilities are owner-scoped ({owner}$Name) and a bare name matched nothing, so indexing it
        // would only have produced false "missing object" diagnostics. Owner-agnostic resolution
        // removed that blocker, so the value navigates now.
        // R_Supply_Dock_Income_Bonus is declared by exactly one object, so the target is
        // unambiguous - unlike R_Comm_Array_Enable_Radar, which two objects declare and where any
        // owner is a legitimate answer.
        await RunGoToAsync(null, "R_Supply_Dock_Income_Bonus", "Starbases",
            "Data/Xml/Hardpoints.xml");
    }

    [Fact]
    public async Task XmlGoTo_AfterOpenCloseCyclesOfTargetFile_StillResolvesWorkspaceDefinition()
    {
        // Regression for the 2026-07-05 didClose bug: the Lua sync handler also received XML
        // didClose notifications and queued an index REMOVAL that raced the XML handler's
        // async re-add - when the removal landed last, the closed file's symbols were silently
        // deleted and every reference into it fell back to the non-navigable baseline. VS Code
        // preview tabs make open+close cycles constant, so navigating files progressively
        // destroyed the index. Cycle the target file a few times, then go-to must still work.
        RequireEawWorkspace();
        await WaitForFullScanAsync();

        var workspace = LspTestEnvironment.EawWorkspacePath!;
        var frigatesPath = Path.Combine(workspace, "Data/Xml/Spaceunitsfrigates.xml");
        var frigatesUri = DocumentUri.FromFileSystemPath(frigatesPath);
        var frigatesText = await File.ReadAllTextAsync(frigatesPath);

        for (var i = 0; i < 3; i++)
        {
            _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem
                    { Uri = frigatesUri, LanguageId = "xml", Version = 1, Text = frigatesText }
            });
            await Task.Delay(100);
            _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = frigatesUri }
            });
            await Task.Delay(150);
        }

        await RunGoToAsync(null, "Nebulon_B_Frigate", "Spaceunitsfrigates", FightersXmlRel);
    }

    [Fact]
    public async Task XmlGoTo_CampaignStoryName_NavigatesToPlotManifestFile()
    {
        // <Empire_Story_Name>Story_Plots_Campaign_Empire.xml</…> is a workspaceFile reference;
        // go-to resolves it to the plot-manifest file it names.
        await RunGoToAsync("Empire_Story_Name", "Story_Plots_Campaign_Empire.xml",
            "Story_plots_campaign_empire.xml", "Data/Xml/Campaigns_Alpha.xml");
    }

    [Fact]
    public async Task XmlGoTo_ManifestActivePlot_NavigatesToStoryThreadFile()
    {
        await RunGoToAsync("Active_Plot", "Story_Campaign_Empire_Act_I.xml",
            "Story_campaign_empire_act_i.xml", "Data/Xml/Story_plots_campaign_empire.xml");
    }

    [Fact]
    public async Task XmlGoTo_ManifestLuaScript_NavigatesToScriptFile()
    {
        // A <Lua_Script> names an extensionless script; go-to resolves across layers to the
        // .lua file's workspace-file symbol emitted by the Lua parser.
        await RunGoToAsync("Lua_Script", "Story_Campaign_Empire_Act_I",
            "Story_campaign_empire_act_i.lua", "Data/Xml/Story_plots_campaign_empire.xml");
    }

    [Fact]
    public async Task XmlGoTo_TacticalEventParam_NavigatesToTacticalPlotManifest()
    {
        // STORY_LAND_TACTICAL Event_Param1 references a tactical plot manifest reached through the
        // story chain; go-to resolves it to the same storyplotmanifest: file-symbol.
        await RunGoToAsync("Event_Param1", "Story_Plots_Empire_ActI_M02_Fondor_LAND.XML",
            "story_plots_empire_acti_m02_fondor_land.xml", "Data/Xml/Story_campaign_empire_act_i.xml");
    }

    // ── Campaign per-faction / force-deployment tuple slots (A1-A3) ───────────

    [Fact]
    public async Task XmlGoTo_HomeLocationFactionSlot_NavigatesToFactionsDefinition()
    {
        // A1: <Home_Location> Rebel, Dantooine </Home_Location> - the faction slot resolves against
        // the Faction pool (registered via factionfiles.xml) and navigates to Factions.xml.
        await RunGoToAsync("Home_Location", "Rebel", "Factions", CampaignsXmlRel);
    }

    [Fact]
    public async Task XmlGoTo_HomeLocationPlanetSlot_NavigatesToPlanetsDefinition()
    {
        // A1: the planet slot of the same pair resolves against GameObjectType and navigates to the
        // <Planet Name="Dantooine"> definition in Planets.xml.
        await RunGoToAsync("Home_Location", "Dantooine", "Planets", CampaignsXmlRel);
    }

    [Fact]
    public async Task XmlGoTo_StartingCreditsFactionSlot_NavigatesToFactionsDefinition()
    {
        // A2: <Starting_Credits> Rebel, 0 </Starting_Credits> - only the faction slot is a
        // reference; the number is left to PerFactionValueHandler.
        await RunGoToAsync("Starting_Credits", "Rebel", "Factions", CampaignsXmlRel);
    }

    [Fact]
    public async Task XmlGoTo_StartingForcesPlanetSlot_NavigatesToPlanetsDefinition()
    {
        // A3: <Starting_Forces> Empire, Anaxes, Empire_Star_Base_1 </Starting_Forces> - the middle
        // (planet) slot of the triple navigates to Planets.xml.
        await RunGoToAsync("Starting_Forces", "Anaxes", "Planets", CampaignsXmlRel);
    }

    [Fact]
    public async Task XmlGoTo_StartingForcesUnitSlot_NavigatesToUnitDefinition()
    {
        // A3: the third (unit) slot of the same triple navigates to the object definition -
        // Empire_Star_Base_1 lives in Starbases.xml.
        await RunGoToAsync("Starting_Forces", "Empire_Star_Base_1", "Starbases", CampaignsXmlRel);
    }

    [Fact]
    public async Task XmlGoTo_MarkupFilenameFactionSlot_NavigatesToFactionsDefinition()
    {
        // A4: <Markup_Filename>Empire, DefaultGalacticHints</Markup_Filename> - only the faction slot
        // is indexable; it navigates to Factions.xml. The markup file half is intentionally inert.
        await RunGoToAsync("Markup_Filename", "Empire", "Factions", CampaignsXmlRel);
    }

    // ── Campaign_Set grouping key (B) ─────────────────────────────────────────

    [Fact]
    public async Task XmlGoTo_CampaignSetGroupKey_NavigatesToTheCoMemberCampaigns()
    {
        // <Campaign_Set> Multiplayer_Campaign_Set </Campaign_Set> is a referenceGroup key shared by
        // seven campaigns in the same file; go-to surfaces the co-members (peek), all defined in
        // Campaigns_multiplayer.xml.
        await RunGoToAsync("Campaign_Set", "Multiplayer_Campaign_Set", "Campaigns_multiplayer",
            "Data/Xml/Campaigns_multiplayer.xml");
    }

    [Fact]
    public async Task XmlFindReferences_OnCampaignSetGroupKey_ReturnsAllCampaignsInTheSet()
    {
        RequireEawWorkspace();
        await WaitForFullScanAsync();

        var workspace = LspTestEnvironment.EawWorkspacePath!;
        var xmlPath = Path.Combine(workspace, "Data/Xml/Campaigns_multiplayer.xml");
        var xmlUri = DocumentUri.FromFileSystemPath(xmlPath);
        var lines = await File.ReadAllLinesAsync(xmlPath);

        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
                { Uri = xmlUri, LanguageId = "xml", Version = 1, Text = string.Join(Environment.NewLine, lines) }
        });
        await Task.Delay(300);

        try
        {
            var (line, col) = FindXmlTagBodyValuePosition(lines, "Campaign_Set", "Multiplayer_Campaign_Set");
            Assert.True(line >= 0, "Could not find the Campaign_Set value in Campaigns_multiplayer.xml");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await _fixture.Client.RequestReferences(new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = xmlUri },
                Position = new Position(line, col),
                Context = new ReferenceContext { IncludeDeclaration = true }
            }, cts.Token);

            Assert.NotNull(result);
            // The multiplayer set groups multiple campaigns - find-references lists them all.
            Assert.True(result!.Count() >= 2,
                $"Expected multiple campaigns in the set, got {result!.Count()}");
        }
        finally
        {
            _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = xmlUri }
            });
        }
    }

    [Fact]
    public async Task XmlFindReferences_OnPlotReference_IncludesTheReferencedFile()
    {
        RequireEawWorkspace();
        await WaitForFullScanAsync();

        var workspace = LspTestEnvironment.EawWorkspacePath!;
        var xmlPath = Path.Combine(workspace, "Data/Xml/Campaigns_Alpha.xml");
        var xmlUri = DocumentUri.FromFileSystemPath(xmlPath);
        var lines = await File.ReadAllLinesAsync(xmlPath);

        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
                { Uri = xmlUri, LanguageId = "xml", Version = 1, Text = string.Join(Environment.NewLine, lines) }
        });
        await Task.Delay(300);

        try
        {
            var (line, col) = FindXmlTagBodyValuePosition(lines, "Empire_Story_Name",
                "Story_Plots_Campaign_Empire.xml");
            Assert.True(line >= 0, "Could not find the plot reference in Campaigns_Alpha.xml");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await _fixture.Client.RequestReferences(new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = xmlUri },
                Position = new Position(line, col),
                Context = new ReferenceContext { IncludeDeclaration = true }
            }, cts.Token);

            Assert.NotNull(result);
            var uris = result!.Select(l => l.Uri.ToString()).ToList();
            Assert.Contains(uris, u => u.Contains("story_plots_campaign_empire.xml", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = xmlUri }
            });
        }
    }

    private async Task RunGoToAsync(string? tagName, string value, string expectedDefinitionFile,
        string sourceFileRel = CorvettesXmlRel)
    {
        RequireEawWorkspace();
        await WaitForFullScanAsync();

        var workspace = LspTestEnvironment.EawWorkspacePath!;
        var xmlPath = Path.Combine(workspace, sourceFileRel);
        var xmlUri = DocumentUri.FromFileSystemPath(xmlPath);
        var lines = await File.ReadAllLinesAsync(xmlPath);

        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = xmlUri, LanguageId = "xml", Version = 1,
                Text = string.Join(Environment.NewLine, lines)
            }
        });
        await Task.Delay(300);

        try
        {
            var (line, col) = tagName is null
                ? FindFirstOccurrencePosition(lines, value)
                : FindXmlTagBodyValuePosition(lines, tagName, value);
            Assert.True(line >= 0,
                $"Could not find '{value}' in {sourceFileRel}");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await _fixture.Client.RequestDefinition(
                new DefinitionParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = xmlUri },
                    Position = new Position(line, col)
                }, cts.Token);

            Assert.NotNull(result);
            // Definitions are returned as LocationLinks (with an originSelectionRange for the Ctrl-hover
            // decoration); tolerate the plain-Location shape too in case the client downgrades.
            var targetUris = result!
                .Select(l => l.IsLocationLink ? l.LocationLink!.TargetUri : l.Location!.Uri)
                .ToList();
            Assert.NotEmpty(targetUris);
            Assert.Contains(targetUris, u =>
                u.ToString().Contains(expectedDefinitionFile, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = xmlUri }
            });
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Position of the first occurrence of <paramref name="value" /> anywhere in the file —
    ///     for list values that do not share a line with their opening tag.
    /// </summary>
    private static (int line, int col) FindFirstOccurrencePosition(string[] lines, string value)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var idx = lines[i].IndexOf(value, StringComparison.Ordinal);
            if (idx >= 0) return (i, idx);
        }

        return (-1, -1);
    }

    private static (int line, int col) FindXmlTagBodyValuePosition(
        string[] lines, string tagName, string value)
    {
        var tagOpen = $"<{tagName}>";
        for (var i = 0; i < lines.Length; i++)
        {
            var tagIdx = lines[i].IndexOf(tagOpen, StringComparison.OrdinalIgnoreCase);
            if (tagIdx < 0) continue;
            var searchFrom = tagIdx + tagOpen.Length;
            var valueIdx = lines[i].IndexOf(value, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (valueIdx < 0) continue;
            return (i, valueIdx);
        }

        return (-1, -1);
    }

    private static void RequireEawWorkspace()
    {
        if (LspTestEnvironment.EawWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception(
                "$XunitDynamicSkip$eaw/ workspace or schema/eaw/ not found.");
    }

    private async Task WaitForFullScanAsync()
    {
        var completed = await Task.WhenAny(_fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(180)));
        if (completed != _fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 180 s.");
    }
}