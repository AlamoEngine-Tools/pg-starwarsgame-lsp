// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     E2E smoke tests for XML rename triggered from usage sites: a dynamic-enum value
///     (anchor-format, Gameconstants.xml's &lt;Armor_Types&gt; list), a dynamic-enum value inside
///     a pipe-separated list (&lt;CategoryMask&gt;), and an object reference inside a
///     comma-separated list (&lt;HardPoints&gt;). Regression coverage for the 2026-07-05 report
///     that global rename of Armor_Types/Damage_Types values does nothing even though
///     Gameconstants.xml is owned by the (leaf) project.
/// </summary>
[Trait("Category", "E2E")]
public sealed class XmlEnumValueRenameSmokeTest : IClassFixture<EawLspServerFixture>
{
    private const string CorvettesXmlRel = "Data/Xml/Spaceunitscorvettes.xml";

    private readonly EawLspServerFixture _fixture;

    public XmlEnumValueRenameSmokeTest(EawLspServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task XmlRename_ArmorTypeEnumValue_FromUsageSite_EditsDefinitionAndUsages()
    {
        // Single-value tag: <Armor_Type> Armor_Tartan </Armor_Type>, anchor-format enum
        // defined in Gameconstants.xml's <Armor_Types> text list.
        await RunRenameAsync("Armor_Type", "Armor_Tartan", "Armor_Tartan_Renamed", "Gameconstants");
    }

    [Fact]
    public async Task XmlRename_CategoryMaskEnumValue_FromPipeListToken_EditsDefinitionAndUsages()
    {
        // <CategoryMask> Corvette | AntiFighter | AntiBomber </CategoryMask> — rename triggered
        // from the second token of a pipe-separated dynamic-enum list; the value is defined in
        // bare <EnumDefinition> format (element name) in Enum/Gameobjectcategorytype.xml.
        await RunRenameAsync(null, "AntiFighter", "AntiFighter_Renamed", "Gameobjectcategorytype");
    }

    [Fact]
    public async Task XmlRename_HardPointObject_FromCommaListToken_EditsDefinitionAndUsages()
    {
        // <HardPoints> HP_Corellian_Corvette_01, HP_Corellian_Corvette_02, … </HardPoints> —
        // rename triggered from the second token of a comma-separated object-reference list;
        // the HardPoint object is defined in Hardpoints.xml.
        await RunRenameAsync(null, "HP_Corellian_Corvette_02", "HP_Corellian_Corvette_02_Renamed",
            "Hardpoints");
    }

    private async Task RunRenameAsync(
        string? tagName, string value, string newName, string expectedDefinitionFile)
    {
        RequireEawWorkspace();
        await WaitForFullScanAsync();

        var workspace = LspTestEnvironment.EawWorkspacePath!;
        var xmlPath = Path.Combine(workspace, CorvettesXmlRel);
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
            Assert.True(line >= 0, $"Could not find '{value}' in Spaceunitscorvettes.xml");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var edit = await _fixture.Client.RequestRename(
                new RenameParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = xmlUri },
                    Position = new Position(line, col),
                    NewName = newName
                }, cts.Token);

            Assert.NotNull(edit);
            var changes = edit!.Changes;
            Assert.NotNull(changes);
            Assert.NotEmpty(changes!);

            // The definition site must be edited …
            Assert.Contains(changes!.Keys, u =>
                u.ToString().Contains(expectedDefinitionFile, StringComparison.OrdinalIgnoreCase));
            // … and so must the usage file the rename was triggered from.
            Assert.Contains(changes!.Keys, u =>
                u.ToString().Contains("Spaceunitscorvettes", StringComparison.OrdinalIgnoreCase));
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
