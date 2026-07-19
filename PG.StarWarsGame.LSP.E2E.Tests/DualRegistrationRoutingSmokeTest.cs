// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Diagnostic E2E test for the dual-registration wiring convention used by Completion and
///     Definition (both <c>Xml*Handler</c> and <c>Lua*Handler</c> registered directly via
///     <c>.WithHandler&lt;&gt;()</c>, discriminated only by <c>DocumentSelector</c>), as opposed to
///     the router convention used by Hover/Rename (<c>Game*Handler</c> delegating to
///     <c>IXml*Provider</c>/<c>ILua*Provider</c>).
///     <para>
///         Opens both an XML and a Lua document in the same live DryIoc-backed server instance and
///         asserts each capability resolves via the correct language's handler with no crosstalk.
///         XML tag-name completion always returns <see cref="CompletionItemKind.Property" /> items;
///         Lua identifier completion of a known engine API function always returns
///         <see cref="CompletionItemKind.Function" />. Neither kind should appear in the other
///         language's result.
///     </para>
/// </summary>
[Trait("Category", "E2E")]
public sealed class DualRegistrationRoutingSmokeTest : IClassFixture<EawLspServerFixture>
{
    private const string InterdictorRel = "Data/Scripts/Gameobject/Interdictor.lua";
    private const string CorvettesXmlRel = "Data/Xml/Spaceunitscorvettes.xml";
    private const string GameconstantsXmlRel = "Data/Xml/Gameconstants.xml";

    private readonly EawLspServerFixture _fixture;

    public DualRegistrationRoutingSmokeTest(EawLspServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Completion_XmlAndLuaDocumentsOpenTogether_RouteToCorrectLanguageHandler()
    {
        RequireEawWorkspace();
        await WaitForFullScanAsync();

        var workspace = LspTestEnvironment.EawWorkspacePath!;
        var xmlPath = Path.Combine(workspace, CorvettesXmlRel);
        var luaPath = Path.Combine(workspace, InterdictorRel);
        var xmlUri = DocumentUri.FromFileSystemPath(xmlPath);
        var luaUri = DocumentUri.FromFileSystemPath(luaPath);
        var xmlLines = await File.ReadAllLinesAsync(xmlPath);
        var luaLines = await File.ReadAllLinesAsync(luaPath);

        try
        {
            _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem
                {
                    Uri = xmlUri, LanguageId = "xml", Version = 1,
                    Text = string.Join(Environment.NewLine, xmlLines)
                }
            });
            _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem
                {
                    Uri = luaUri, LanguageId = "lua", Version = 1,
                    Text = string.Join(Environment.NewLine, luaLines)
                }
            });
            await Task.Delay(300);

            // ── XML: tag-name completion inside <SpaceUnit> ────────────────────
            var (xmlLine, xmlCol) = FindFirstGrandchildElementPosition(xmlLines);
            using var xmlCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var xmlResult = await _fixture.Client.RequestCompletion(
                new CompletionParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = xmlUri },
                    Position = new Position(xmlLine, xmlCol),
                    Context = new CompletionContext
                    {
                        TriggerKind = CompletionTriggerKind.TriggerCharacter,
                        TriggerCharacter = "<"
                    }
                }, xmlCts.Token);

            Assert.NotNull(xmlResult);
            Assert.NotEmpty(xmlResult.Items);
            Assert.All(xmlResult.Items, i => Assert.Equal(CompletionItemKind.Property, i.Kind));
            Assert.DoesNotContain(xmlResult.Items, i => i.Kind == CompletionItemKind.Function);

            // ── Lua: identifier completion of a known engine API function ──────
            var (luaLine, luaCol) = FindIdentifierEndBeforeParen(luaLines, "Find_Object_Type");
            Assert.True(luaLine >= 0, "Could not find 'Find_Object_Type(' in Interdictor.lua");

            using var luaCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var luaResult = await _fixture.Client.RequestCompletion(
                new CompletionParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = luaUri },
                    Position = new Position(luaLine, luaCol)
                }, luaCts.Token);

            Assert.NotNull(luaResult);
            Assert.Contains(luaResult.Items, i =>
                i.Label == "Find_Object_Type" && i.Kind == CompletionItemKind.Function);
            Assert.DoesNotContain(luaResult.Items, i => i.Kind == CompletionItemKind.Property);
        }
        finally
        {
            _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = xmlUri }
            });
            _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = luaUri }
            });
        }
    }

    [Fact]
    public async Task Definition_XmlAndLuaDocumentsOpenTogether_RouteToCorrectLanguageHandler()
    {
        RequireEawWorkspace();
        await WaitForFullScanAsync();

        var workspace = LspTestEnvironment.EawWorkspacePath!;
        var xmlPath = Path.Combine(workspace, CorvettesXmlRel);
        var luaPath = Path.Combine(workspace, InterdictorRel);
        var xmlUri = DocumentUri.FromFileSystemPath(xmlPath);
        var luaUri = DocumentUri.FromFileSystemPath(luaPath);
        var xmlLines = await File.ReadAllLinesAsync(xmlPath);
        var luaLines = await File.ReadAllLinesAsync(luaPath);

        try
        {
            _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem
                {
                    Uri = xmlUri, LanguageId = "xml", Version = 1,
                    Text = string.Join(Environment.NewLine, xmlLines)
                }
            });
            _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
            {
                TextDocument = new TextDocumentItem
                {
                    Uri = luaUri, LanguageId = "lua", Version = 1,
                    Text = string.Join(Environment.NewLine, luaLines)
                }
            });
            await Task.Delay(300);

            // ── Lua → Xml: Find_Object_Type("Marauder_Missile_Cruiser") ────────
            var (luaLine, luaCol) =
                FindLuaStringArgPosition(luaLines, "Find_Object_Type", "Marauder_Missile_Cruiser");
            Assert.True(luaLine >= 0,
                "Could not find Find_Object_Type(\"Marauder_Missile_Cruiser\") in Interdictor.lua");

            using var luaCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var luaResult = await _fixture.Client.RequestDefinition(
                new DefinitionParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = luaUri },
                    Position = new Position(luaLine, luaCol)
                }, luaCts.Token);

            Assert.NotNull(luaResult);
            var luaLocations = luaResult!.Select(l => l.Location!).ToList();
            Assert.NotEmpty(luaLocations);
            Assert.Contains(luaLocations, l =>
                l.Uri.ToString().Contains("Spaceunitscorvettes", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(luaLocations, l =>
                l.Uri.ToString().EndsWith(".lua", StringComparison.OrdinalIgnoreCase));

            // ── Xml → Xml (cross-file): <Armor_Type> Armor_Tartan </Armor_Type> ─
            var (xmlLine, xmlCol) = FindXmlTagBodyValuePosition(xmlLines, "Armor_Type", "Armor_Tartan");
            Assert.True(xmlLine >= 0,
                "Could not find <Armor_Type> Armor_Tartan </Armor_Type> in Spaceunitscorvettes.xml");

            using var xmlCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var xmlResult = await _fixture.Client.RequestDefinition(
                new DefinitionParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = xmlUri },
                    Position = new Position(xmlLine, xmlCol)
                }, xmlCts.Token);

            Assert.NotNull(xmlResult);
            var xmlLocations = xmlResult!.Select(l => l.Location!).ToList();
            Assert.NotEmpty(xmlLocations);
            Assert.Contains(xmlLocations, l =>
                l.Uri.ToString().Contains(GameconstantsXmlRel.Split('/')[^1],
                    StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(xmlLocations, l =>
                l.Uri.ToString().EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = xmlUri }
            });
            _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = luaUri }
            });
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Returns the position of the first grandchild element - the first field tag
    ///     inside the first type container - so tag-name completion hits a known context.
    /// </summary>
    private static (int line, int col) FindFirstGrandchildElementPosition(string[] lines)
    {
        var firstChildLine = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var s = lines[i];
            var lt = s.IndexOf('<');
            if (lt <= 0) continue;
            if (s.Length <= lt + 1) continue;
            var next = s[lt + 1];
            if (next == '/' || next == '?' || next == '!') continue;
            firstChildLine = i;
            break;
        }

        if (firstChildLine < 0) return (1, 1);

        for (var i = firstChildLine + 1; i < lines.Length; i++)
        {
            var s = lines[i];
            var lt = s.IndexOf('<');
            if (lt < 0) continue;
            if (s.Length <= lt + 1) continue;
            var next = s[lt + 1];
            if (next == '/' || next == '?' || next == '!') continue;
            return (i, lt + 1);
        }

        return (1, 1);
    }

    /// <summary>
    ///     Returns the 0-based (line, column) of the first character of <paramref name="value" />
    ///     inside a call like <c>funcName("value")</c>.
    /// </summary>
    private static (int line, int col) FindLuaStringArgPosition(
        string[] lines, string funcName, string value)
    {
        var marker = $"{funcName}(\"{value}\"";
        for (var i = 0; i < lines.Length; i++)
        {
            var idx = lines[i].IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            return (i, idx + funcName.Length + 2); // +2 for '(' and '"'
        }

        return (-1, -1);
    }

    /// <summary>
    ///     Returns the 0-based (line, column) immediately after <paramref name="identifier" />
    ///     in a call like <c>identifier(...)</c> - i.e. a position inside the identifier token
    ///     itself, so it classifies as a Lua identifier-completion context.
    /// </summary>
    private static (int line, int col) FindIdentifierEndBeforeParen(string[] lines, string identifier)
    {
        var marker = $"{identifier}(";
        for (var i = 0; i < lines.Length; i++)
        {
            var idx = lines[i].IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            return (i, idx + identifier.Length);
        }

        return (-1, -1);
    }

    /// <summary>
    ///     Returns the 0-based (line, column) of the first character of <paramref name="value" />
    ///     inside an XML element body like <c>&lt;tagName&gt; value &lt;/tagName&gt;</c>.
    /// </summary>
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
                "$XunitDynamicSkip$eaw/ workspace or schema/eaw/ not found - cannot run dual-registration routing tests.");
    }

    private async Task WaitForFullScanAsync()
    {
        var completed = await Task.WhenAny(_fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(180)));
        if (completed != _fixture.ScanCompleted)
            throw new Exception(
                "$XunitDynamicSkip$Workspace scan did not complete within 180 s.");
    }
}