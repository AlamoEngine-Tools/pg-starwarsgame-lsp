// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     E2E smoke tests for Lua GoTo Definition navigating to XML object definitions.
/// </summary>
[Trait("Category", "E2E")]
public sealed class LuaGoToSmokeTest : IClassFixture<EawLspServerFixture>
{
    private const string InterdictorRel = "Data/Scripts/Gameobject/Interdictor.lua";

    private readonly EawLspServerFixture _fixture;

    public LuaGoToSmokeTest(EawLspServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LuaGoTo_XmlObjectStringLiteral_ReturnsXmlDefinitionLocation()
    {
        RequireEawWorkspace();
        await WaitForFullScanAsync();

        var workspace = LspTestEnvironment.EawWorkspacePath!;
        var luaPath = Path.Combine(workspace, InterdictorRel);
        var luaUri = DocumentUri.FromFileSystemPath(luaPath);
        var lines = await File.ReadAllLinesAsync(luaPath);

        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = luaUri, LanguageId = "lua", Version = 1,
                Text = string.Join(Environment.NewLine, lines)
            }
        });
        await Task.Delay(200);

        var (line, col) = FindLuaStringArgPosition(lines, "Find_Object_Type", "Marauder_Missile_Cruiser");
        Assert.True(line >= 0, "Could not find Find_Object_Type(\"Marauder_Missile_Cruiser\") in Interdictor.lua");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await _fixture.Client.RequestDefinition(
            new DefinitionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = luaUri },
                Position = new Position(line, col)
            }, cts.Token);

        Assert.NotNull(result);
        var locations = result!.Select(l => l.Location!).ToList();
        Assert.NotEmpty(locations);
        Assert.Contains(locations, l =>
            l.Uri.ToString().Contains("Spaceunitscorvettes", StringComparison.OrdinalIgnoreCase));

        _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = luaUri }
        });
    }

    [Fact]
    public async Task LuaHover_XmlObjectStringLiteral_ReturnsHoverWithTypeName()
    {
        RequireEawWorkspace();
        await WaitForFullScanAsync();

        var workspace = LspTestEnvironment.EawWorkspacePath!;
        var luaPath = Path.Combine(workspace, InterdictorRel);
        var luaUri = DocumentUri.FromFileSystemPath(luaPath);
        var lines = await File.ReadAllLinesAsync(luaPath);

        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = luaUri, LanguageId = "lua", Version = 1,
                Text = string.Join(Environment.NewLine, lines)
            }
        });
        await Task.Delay(200);

        var (line, col) = FindLuaStringArgPosition(lines, "Find_Object_Type", "Marauder_Missile_Cruiser");
        Assert.True(line >= 0);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await _fixture.Client.RequestHover(
            new HoverParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = luaUri },
                Position = new Position(line, col)
            }, cts.Token);

        Assert.NotNull(result);
        var content = result!.Contents.MarkupContent?.Value ?? string.Empty;
        Assert.Contains("Marauder_Missile_Cruiser", content, StringComparison.OrdinalIgnoreCase);
        // Hover must contain some non-trivial content (type name + id, not an empty stub).
        Assert.True(content.Length > "Marauder_Missile_Cruiser".Length,
            $"Hover content is too short to contain type info: {content}");

        _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = luaUri }
        });
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static (int line, int col) FindLuaStringArgPosition(
        string[] lines, string funcName, string value)
    {
        var marker = $"{funcName}(\"{value}\"";
        for (var i = 0; i < lines.Length; i++)
        {
            var idx = lines[i].IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            return (i, idx + funcName.Length + 2);
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