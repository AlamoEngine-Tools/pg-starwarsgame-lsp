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
        // <CategoryMask> Corvette | AntiFighter | AntiBomber </CategoryMask> — dynamic-enum list,
        // pipe-separated; the second token must navigate to the workspace enum definition file.
        await RunGoToAsync(null, "AntiFighter", "Gameobjectcategorytype");
    }

    [Fact]
    public async Task XmlGoTo_FrigateFromSpaceSeparatedEncyclopediaList_NeverOpenedTargetFile_Works()
    {
        // Exact user-reported scenario (2026-07-05): from an open Spaceunitsfighters.xml,
        // <Encyclopedia_Good_Against> Calamari_Cruiser Alliance_Assault_Frigate Nebulon_B_Frigate …
        // (space-separated list) must navigate to Spaceunitsfrigates.xml even though that file
        // was never opened in the editor — its symbols come purely from the workspace scan.
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
    public async Task XmlGoTo_AfterOpenCloseCyclesOfTargetFile_StillResolvesWorkspaceDefinition()
    {
        // Regression for the 2026-07-05 didClose bug: the Lua sync handler also received XML
        // didClose notifications and queued an index REMOVAL that raced the XML handler's
        // async re-add — when the removal landed last, the closed file's symbols were silently
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
            var locations = result!.Select(l => l.Location!).ToList();
            Assert.NotEmpty(locations);
            Assert.Contains(locations, l =>
                l.Uri.ToString().Contains(expectedDefinitionFile, StringComparison.OrdinalIgnoreCase));
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
