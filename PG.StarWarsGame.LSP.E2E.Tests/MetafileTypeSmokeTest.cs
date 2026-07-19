// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Smoke tests for game object types registered via metafiles.
///     All tests open real workspace files from <c>foc/Data/XML/</c> and require the
///     workspace scan to have completed so that the FileTypeRegistry is populated.
///     The smallest available file listed in each metafile is used for generic tests.
/// </summary>
[Trait("Category", "E2E")]
public sealed class MetafileTypeSmokeTest : IClassFixture<LspServerFixture>, IAsyncDisposable
{
    private readonly LspServerFixture _fixture;
    private readonly List<DocumentUri> _openedUris = [];

    public MetafileTypeSmokeTest(LspServerFixture fixture)
    {
        _fixture = fixture;
    }

    private static string XmlDir =>
        Path.Combine(LspTestEnvironment.WorkspacePath!, "Data", "XML");

    /// <summary>
    ///     Closes all documents opened during this test so that large files (e.g.
    ///     CommandBarComponents.xml) do not accumulate across tests and cause
    ///     subsequent RunPublish calls to time out.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var uri in _openedUris)
            _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri }
            });

        if (_openedUris.Count > 0)
            await Task.Delay(500);
    }

    // ── GameObjectType (CIN_SpaceUnitsFrigates.xml - 757 B) ──────────────────

    [Fact]
    public async Task GameObjectType_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "CIN_SpaceUnitsFrigates.xml"));
        var (line, col) = FindFirstChildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("GameObjectType", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("All game object types", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GameObjectType_TagHover_ReturnsTagMarkdown()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "CIN_SpaceUnitsFrigates.xml"));
        var (line, col) = FindFirstGrandchildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        Assert.Contains("Cinematic_Object_Only", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GameObjectType_Diagnostics_PublishedOnOpen()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var filePath = Path.Combine(XmlDir, "CIN_SpaceUnitsFrigates.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));
        await OpenAndWaitAsync(filePath);
        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── Faction (Expansion_Factions.xml - 60 KB) ─────────────────────────────

    [Fact]
    public async Task Faction_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "Expansion_Factions.xml"));
        var (line, col) = FindFirstChildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("Faction", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A playable or AI faction", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Faction_TagHover_ReturnsTagMarkdown()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "Expansion_Factions.xml"));
        var (line, col) = FindFirstGrandchildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        Assert.Contains("Is_Debug_Switchable_To", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Faction_Diagnostics_PublishedOnOpen()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var filePath = Path.Combine(XmlDir, "Expansion_Factions.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));
        await OpenAndWaitAsync(filePath);
        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── Campaign (Campaigns_Underworld_Tutorial.xml - 7 KB) ──────────────────

    [Fact]
    public async Task Campaign_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "Campaigns_Underworld_Tutorial.xml"));
        var (line, col) = FindFirstChildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("Campaign", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A campaign definition", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Campaign_TagHover_ReturnsTagMarkdown()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "Campaigns_Underworld_Tutorial.xml"));
        var (line, col) = FindFirstGrandchildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        Assert.Contains("Campaign_Set", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Campaign_Diagnostics_PublishedOnOpen()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var filePath = Path.Combine(XmlDir, "Campaigns_Underworld_Tutorial.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));
        await OpenAndWaitAsync(filePath);
        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── CommandBarComponent (CommandBarComponents.xml) ────────────────────────

    [Fact]
    public async Task CommandBarComponent_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "CommandBarComponents.xml"));
        var (line, col) = FindFirstChildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("CommandBarComponent", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A command bar UI component", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommandBarComponent_TagHover_ReturnsTagMarkdown()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "CommandBarComponents.xml"));
        var (line, col) = FindFirstGrandchildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        Assert.Contains("Model_Name", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommandBarComponent_Diagnostics_PublishedOnOpen()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var filePath = Path.Combine(XmlDir, "CommandBarComponents.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        // CommandBarComponents.xml is 683 KB - allow extra time in debug builds.
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(60));
        await OpenAndWaitAsync(filePath);
        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── TargetingPrioritySet (TurretTargetingPriorities.xml - 1 KB) ──────────

    [Fact]
    public async Task TargetingPrioritySet_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "TurretTargetingPriorities.xml"));
        var (line, col) = FindFirstChildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("TargetingPrioritySet", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A prioritised set of target categories", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TargetingPrioritySet_TagHover_ReturnsTagMarkdown()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "TurretTargetingPriorities.xml"));
        var (line, col) = FindFirstGrandchildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        Assert.Contains("Attack_Priorities", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TargetingPrioritySet_Diagnostics_PublishedOnOpen()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var filePath = Path.Combine(XmlDir, "TurretTargetingPriorities.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));
        await OpenAndWaitAsync(filePath);
        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── TradeRoute (TradeRoutes.xml) ──────────────────────────────────────────

    [Fact]
    public async Task TradeRoute_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "TradeRoutes.xml"));
        var (line, col) = FindFirstChildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("TradeRoute", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trade route connecting two planets", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TradeRoute_TagHover_ReturnsTagMarkdown()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "TradeRoutes.xml"));
        var (line, col) = FindFirstGrandchildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        Assert.Contains("Point_A", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TradeRoute_Diagnostics_PublishedOnOpen()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var filePath = Path.Combine(XmlDir, "TradeRoutes.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));
        await OpenAndWaitAsync(filePath);
        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── BinkMovie (Movies.xml - directContent) ────────────────────────────────

    [Fact]
    public async Task BinkMovie_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "Movies.xml"));
        var (line, col) = FindFirstChildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("BinkMovie", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bink video file reference", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BinkMovie_TagHover_ReturnsTagMarkdown()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "Movies.xml"));
        var (line, col) = FindFirstGrandchildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        Assert.Contains("Movie_File", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BinkMovie_Diagnostics_PublishedOnOpen()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var filePath = Path.Combine(XmlDir, "Movies.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));
        await OpenAndWaitAsync(filePath);
        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── MusicEvent (Musicevents.xml - directContent) ──────────────────────────

    [Fact]
    public async Task MusicEvent_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "Musicevents.xml"));
        var (line, col) = FindFirstChildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("MusicEvent", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("music track or event definition", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MusicEvent_TagHover_ReturnsTagMarkdown()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "Musicevents.xml"));
        var (line, col) = FindFirstGrandchildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        Assert.Contains("Files", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MusicEvent_Diagnostics_PublishedOnOpen()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var filePath = Path.Combine(XmlDir, "Musicevents.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));
        await OpenAndWaitAsync(filePath);
        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── HardPoint (CIN_HardPoints.xml - 14 KB) ───────────────────────────────

    [Fact]
    public async Task HardPoint_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "CIN_HardPoints.xml"));
        var (line, col) = FindFirstChildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("HardPoint", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("weapon or component hardpoint", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HardPoint_TagHover_ReturnsTagMarkdown()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "CIN_HardPoints.xml"));
        var (line, col) = FindFirstGrandchildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        // First grandchild in CIN_HardPoints.xml is <Type>.
        Assert.Contains("HardPoint", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HardPoint_Diagnostics_PublishedOnOpen()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var filePath = Path.Combine(XmlDir, "CIN_HardPoints.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));
        await OpenAndWaitAsync(filePath);
        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── Story chain: plot manifest + story thread discovered from campaignfiles.xml ──
    // Campaigns_Underworld_Story.xml → Story_Plots_Campaign_Underworld.XML (manifest)
    // → Story_Campaign_Underworld.xml (thread). Both files are only typed when the
    // MetafileType.Special chain scan ran during startup.

    [Fact]
    public async Task StoryPlotManifest_TagHover_ReturnsTagMarkdown()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "Story_plots_campaign_underworld.xml"));
        var (line, col) = FindFirstChildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        // First child in Story_plots_campaign_underworld.xml is <Lua_Script>.
        Assert.Contains("Lua_Script", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoryPlotManifest_Diagnostics_PublishedOnOpen()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var filePath = Path.Combine(XmlDir, "Story_plots_campaign_underworld.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));
        await OpenAndWaitAsync(filePath);
        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    [Fact]
    public async Task StoryParser_TagHover_ReturnsTagMarkdown()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var (uri, lines) = await OpenAndWaitAsync(Path.Combine(XmlDir, "Story_campaign_underworld.xml"));
        var (line, col) = FindFirstGrandchildElementPosition(lines);
        var result = await HoverAsync(uri, line, col);

        Assert.NotNull(result);
        // First grandchild in Story_campaign_underworld.xml is <Event_Type>.
        Assert.Contains("trigger condition type", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoryParser_Diagnostics_PublishedOnOpen()
    {
        RequireWorkspace();
        await WaitForScanAsync();
        var filePath = Path.Combine(XmlDir, "Story_campaign_underworld.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));
        await OpenAndWaitAsync(filePath);
        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<(DocumentUri uri, string[] lines)> OpenAndWaitAsync(string filePath)
    {
        var uri = DocumentUri.FromFileSystemPath(filePath);
        _openedUris.Add(uri);
        var lines = await File.ReadAllLinesAsync(filePath);

        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "xml",
                Version = 1,
                Text = string.Join(Environment.NewLine, lines)
            }
        });

        // Wait for the server to receive and dispatch DidOpen so AddOrUpdate has run
        // before the subsequent hover request is sent.
        await Task.Delay(200);
        return (uri, lines);
    }

    private async Task<Hover?> HoverAsync(DocumentUri uri, int line, int col)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await _fixture.Client.RequestHover(
            new HoverParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = new Position(line, col)
            }, cts.Token);
    }

    private static string MarkdownOf(Hover hover)
    {
        return hover.Contents.MarkupContent?.Value ?? string.Empty;
    }

    private Task<PublishDiagnosticsParams> WaitForDiagnosticsAsync(DocumentUri uri, TimeSpan timeout)
    {
        return _fixture.WaitForDiagnosticsAsync(uri, timeout);
    }

    private static void RequireWorkspace()
    {
        if (LspTestEnvironment.WorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception(
                "$XunitDynamicSkip$Set LSP_WORKSPACE_PATH and LSP_SCHEMA_LOCAL_PATH to run this test.");
    }

    private async Task WaitForScanAsync()
    {
        var completed = await Task.WhenAny(_fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(60)));
        if (completed != _fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not complete within 60 s.");
    }

    /// <summary>
    ///     Returns the position of the first indented child element.
    ///     Root elements and XML declarations (col 0) are skipped.
    /// </summary>
    private static (int line, int col) FindFirstChildElementPosition(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var s = lines[i];
            var lt = s.IndexOf('<');
            if (lt <= 0) continue;
            if (s.Length <= lt + 1) continue;
            var next = s[lt + 1];
            if (next == '/' || next == '?' || next == '!') continue;
            return (i, lt + 1);
        }

        return (1, 1);
    }

    /// <summary>
    ///     Returns the position of the first grandchild element (first tag inside the first
    ///     type instance). Works regardless of indentation style (tabs vs spaces).
    /// </summary>
    private static (int line, int col) FindFirstGrandchildElementPosition(string[] lines)
    {
        // Locate the first child (depth-1) opening element.
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

        // The first opening element after the first child's opening tag is the first grandchild.
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
}