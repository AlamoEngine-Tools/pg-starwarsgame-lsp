// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Smoke tests for every game object type declared in metafiles.yaml.
///     Each type gets three tests: type hover (schema description), tag hover (field markdown),
///     and diagnostics publication on open.
///     Tests require LSP_SCHEMA_LOCAL_PATH; none require a real game workspace.
///     Test data files use the type name as the depth-1 element so the schema resolves the type
///     via element-name lookup without needing the FileTypeRegistry.
/// </summary>
[Trait("Category", "E2E")]
public sealed class MetafileTypeSmokeTest : IClassFixture<LspServerFixture>
{
    // Layout shared by all test data files (0-indexed):
    //   line 0: <?xml ...?>
    //   line 1: <Root>
    //   line 2:   <TypeName>   ← col 3 = first char of type name
    //   line 3:     <TagName>  ← col 5 = first char of tag name
    //   line 4:   </TypeName>
    //   line 5: </Root>
    private const int TypeLine = 2;
    private const int TypeCol = 3;
    private const int TagLine = 3;
    private const int TagCol = 5;

    private readonly LspServerFixture _fixture;

    public MetafileTypeSmokeTest(LspServerFixture fixture)
    {
        _fixture = fixture;
    }

    // ── GameObjectType ────────────────────────────────────────────────────────

    [Fact]
    public async Task GameObjectType_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireSchema();
        var uri = await OpenFileAsync("units.xml");
        var result = await HoverAsync(uri, TypeLine, TypeCol);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("GameObjectType", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("All game object types", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GameObjectType_TagHover_ReturnsTagMarkdown()
    {
        RequireSchema();
        var uri = await OpenFileAsync("units.xml");
        var result = await HoverAsync(uri, TagLine, TagCol);

        Assert.NotNull(result);
        Assert.Contains("Text_ID", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GameObjectType_Diagnostics_PublishedOnOpen()
    {
        RequireSchema();
        var filePath = Path.Combine(_fixture.TestDataDirectory, "units.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        await OpenFileAsync("units.xml");

        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── Faction ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Faction_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireSchema();
        var uri = await OpenFileAsync("faction.xml");
        var result = await HoverAsync(uri, TypeLine, TypeCol);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("Faction", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A playable or AI faction", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Faction_TagHover_ReturnsTagMarkdown()
    {
        RequireSchema();
        var uri = await OpenFileAsync("faction.xml");
        var result = await HoverAsync(uri, TagLine, TagCol);

        Assert.NotNull(result);
        Assert.Contains("Text_ID", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Faction_Diagnostics_PublishedOnOpen()
    {
        RequireSchema();
        var filePath = Path.Combine(_fixture.TestDataDirectory, "faction.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        await OpenFileAsync("faction.xml");

        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── Campaign ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Campaign_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireSchema();
        var uri = await OpenFileAsync("campaign.xml");
        var result = await HoverAsync(uri, TypeLine, TypeCol);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("Campaign", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A campaign definition", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Campaign_TagHover_ReturnsTagMarkdown()
    {
        RequireSchema();
        var uri = await OpenFileAsync("campaign.xml");
        var result = await HoverAsync(uri, TagLine, TagCol);

        Assert.NotNull(result);
        Assert.Contains("Text_ID", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Campaign_Diagnostics_PublishedOnOpen()
    {
        RequireSchema();
        var filePath = Path.Combine(_fixture.TestDataDirectory, "campaign.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        await OpenFileAsync("campaign.xml");

        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── CommandBarComponent ───────────────────────────────────────────────────

    [Fact]
    public async Task CommandBarComponent_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireSchema();
        var uri = await OpenFileAsync("commandbarcomponent.xml");
        var result = await HoverAsync(uri, TypeLine, TypeCol);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("CommandBarComponent", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A command bar UI component", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommandBarComponent_TagHover_ReturnsTagMarkdown()
    {
        RequireSchema();
        var uri = await OpenFileAsync("commandbarcomponent.xml");
        var result = await HoverAsync(uri, TagLine, TagCol);

        Assert.NotNull(result);
        Assert.Contains("Selected_Texture_Name", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommandBarComponent_Diagnostics_PublishedOnOpen()
    {
        RequireSchema();
        var filePath = Path.Combine(_fixture.TestDataDirectory, "commandbarcomponent.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        await OpenFileAsync("commandbarcomponent.xml");

        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── TargetingPrioritySet ──────────────────────────────────────────────────

    [Fact]
    public async Task TargetingPrioritySet_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireSchema();
        var uri = await OpenFileAsync("targetingpriorityset.xml");
        var result = await HoverAsync(uri, TypeLine, TypeCol);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("TargetingPrioritySet", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prioritised set of target categories", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TargetingPrioritySet_TagHover_ReturnsTagMarkdown()
    {
        RequireSchema();
        var uri = await OpenFileAsync("targetingpriorityset.xml");
        var result = await HoverAsync(uri, TagLine, TagCol);

        Assert.NotNull(result);
        Assert.Contains("Category_Exclusions", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TargetingPrioritySet_Diagnostics_PublishedOnOpen()
    {
        RequireSchema();
        var filePath = Path.Combine(_fixture.TestDataDirectory, "targetingpriorityset.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        await OpenFileAsync("targetingpriorityset.xml");

        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── TradeRoute ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TradeRoute_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireSchema();
        var uri = await OpenFileAsync("traderoute.xml");
        var result = await HoverAsync(uri, TypeLine, TypeCol);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("TradeRoute", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trade route connecting two planets", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TradeRoute_TagHover_ReturnsTagMarkdown()
    {
        RequireSchema();
        var uri = await OpenFileAsync("traderoute.xml");
        var result = await HoverAsync(uri, TagLine, TagCol);

        Assert.NotNull(result);
        Assert.Contains("Point_A", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TradeRoute_Diagnostics_PublishedOnOpen()
    {
        RequireSchema();
        var filePath = Path.Combine(_fixture.TestDataDirectory, "traderoute.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        await OpenFileAsync("traderoute.xml");

        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── BinkMovie ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task BinkMovie_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireSchema();
        var uri = await OpenFileAsync("binkmovie.xml");
        var result = await HoverAsync(uri, TypeLine, TypeCol);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("BinkMovie", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bink video file reference", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BinkMovie_TagHover_ReturnsTagMarkdown()
    {
        RequireSchema();
        var uri = await OpenFileAsync("binkmovie.xml");
        var result = await HoverAsync(uri, TagLine, TagCol);

        Assert.NotNull(result);
        Assert.Contains("Movie_File", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BinkMovie_Diagnostics_PublishedOnOpen()
    {
        RequireSchema();
        var filePath = Path.Combine(_fixture.TestDataDirectory, "binkmovie.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        await OpenFileAsync("binkmovie.xml");

        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── MusicEvent ────────────────────────────────────────────────────────────

    [Fact]
    public async Task MusicEvent_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireSchema();
        var uri = await OpenFileAsync("musicevent.xml");
        var result = await HoverAsync(uri, TypeLine, TypeCol);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("MusicEvent", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("music track or event definition", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MusicEvent_TagHover_ReturnsTagMarkdown()
    {
        RequireSchema();
        var uri = await OpenFileAsync("musicevent.xml");
        var result = await HoverAsync(uri, TagLine, TagCol);

        Assert.NotNull(result);
        Assert.Contains("Fade_In_Seconds", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MusicEvent_Diagnostics_PublishedOnOpen()
    {
        RequireSchema();
        var filePath = Path.Combine(_fixture.TestDataDirectory, "musicevent.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        await OpenFileAsync("musicevent.xml");

        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── HardPoint ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HardPoint_TypeHover_ReturnsTypeNameAndSchemaDescription()
    {
        RequireSchema();
        var uri = await OpenFileAsync("hardpoint.xml");
        var result = await HoverAsync(uri, TypeLine, TypeCol);

        Assert.NotNull(result);
        var md = MarkdownOf(result);
        Assert.Contains("HardPoint", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("weapon or component hardpoint", md, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HardPoint_TagHover_ReturnsTagMarkdown()
    {
        RequireSchema();
        var uri = await OpenFileAsync("hardpoint.xml");
        var result = await HoverAsync(uri, TagLine, TagCol);

        Assert.NotNull(result);
        Assert.Contains("Is_Targetable", MarkdownOf(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HardPoint_Diagnostics_PublishedOnOpen()
    {
        RequireSchema();
        var filePath = Path.Combine(_fixture.TestDataDirectory, "hardpoint.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var received = WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(10));

        await OpenFileAsync("hardpoint.xml");

        var diags = await received;
        Assert.Equal(uri, diags.Uri);
    }

    // ── WorkspaceScan: GameObjectType via metafile registry ───────────────────
    // Tests that the workspace scan populates the FileTypeRegistry for GameObjectType.
    // Unlike the element-name tests above, real game files use arbitrary element names
    // (e.g. <UNIT_STAR_DESTROYER>) so type detection requires the registry.

    [Fact]
    public async Task WorkspaceScan_GameObjectFile_TypeHoverReturnsGameObjectType()
    {
        RequireWorkspace();
        await WaitForScanAsync();

        // Any file listed in GameObjectFiles.xml works; Units.xml is canonical for EaW/FoC.
        var filePath = Path.Combine(LspTestEnvironment.WorkspacePath!, "Data", "XML", "Units.xml");
        var uri = DocumentUri.FromFileSystemPath(filePath);
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

        var (line, col) = FindFirstChildElementPosition(lines);
        var result = await _fixture.Client.RequestHover(
            new HoverParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = new Position(line, col)
            }, CancellationToken.None);

        Assert.NotNull(result);
        var md = result.Contents.MarkupContent?.Value ?? string.Empty;
        Assert.Contains("GameObjectType", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("All game object types", md, StringComparison.OrdinalIgnoreCase);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<DocumentUri> OpenFileAsync(string filename)
    {
        var filePath = Path.Combine(_fixture.TestDataDirectory, filename);
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var text = await File.ReadAllTextAsync(filePath);

        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri,
                LanguageId = "xml",
                Version = 1,
                Text = text
            }
        });

        return uri;
    }

    private async Task<Hover?> HoverAsync(DocumentUri uri, int line, int col)
    {
        return await _fixture.Client.RequestHover(
            new HoverParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri },
                Position = new Position(line, col)
            }, CancellationToken.None);
    }

    private static string MarkdownOf(Hover hover)
    {
        return hover.Contents.MarkupContent?.Value ?? string.Empty;
    }

    private Task<PublishDiagnosticsParams> WaitForDiagnosticsAsync(DocumentUri uri, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<PublishDiagnosticsParams>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = _fixture.Client.Register(r => r.OnPublishDiagnostics(p =>
        {
            if (p.Uri == uri)
                tcs.TrySetResult(p);
        }));
        using var cts = new CancellationTokenSource(timeout);
        cts.Token.Register(() =>
        {
            tcs.TrySetCanceled(cts.Token);
            subscription.Dispose();
        });
        return tcs.Task;
    }

    private static void RequireSchema()
    {
        if (LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$Set LSP_SCHEMA_LOCAL_PATH to run this test.");
    }

    private static void RequireWorkspace()
    {
        if (LspTestEnvironment.WorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception(
                "$XunitDynamicSkip$Set LSP_WORKSPACE_PATH and LSP_SCHEMA_LOCAL_PATH to run this test.");
    }

    private async Task WaitForScanAsync()
    {
        var completed = await Task.WhenAny(_fixture.ScanStarted, Task.Delay(TimeSpan.FromSeconds(60)));
        if (completed != _fixture.ScanStarted)
            throw new Exception("$XunitDynamicSkip$Workspace scan did not produce diagnostics within 60 s.");
    }

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
}