// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     Full rename-roundtrip smoke tests against the <c>eaw/</c> workspace.
///     Each test renames Marauder_Missile_Cruiser → Rename_Test, verifies the WorkspaceEdit and
///     diagnostics, then renames back and verifies again.
///     <para>
///         If all four pass, the server is correct; any visible breakage in the VS Code extension
///         is an extension/launch issue rather than a server bug.
///         If a test fails, the assertion message indicates exactly which pipeline stage is broken.
///     </para>
/// </summary>
[Trait("Category", "E2E")]
public sealed class RenameRoundtripSmokeTest : IClassFixture<EawLspServerFixture>
{
    private const string OriginalName = "Marauder_Missile_Cruiser";
    private const string NewName = "Rename_Test";

    private const string InterdictorRel = "Data/Scripts/Gameobject/Interdictor.lua";
    private const string CorvettesXmlRel = "Data/Xml/Spaceunitscorvettes.xml";

    // Lua files that contain "Marauder_Missile_Cruiser" as a string-literal argument
    // to EaW API functions (exact symbol; trailing-underscore variants are separate symbols).
    private static readonly string[] KnownLuaFiles =
    [
        "Data/Scripts/Gameobject/Interdictor.lua",
        "Data/Scripts/Interventions/Intervention_accumulate_credits.lua",
        "Data/Scripts/Interventions/Intervention_conquer_pirate_planet.lua",
        "Data/Scripts/Story/Story_empire_activ_m10_space.lua",
        "Data/Scripts/Story/Story_campaign_empire_act_iii.lua",
        "Data/Scripts/Library/Pgevents.lua",
        "Data/Scripts/Ai/Spacemode/Tacticalmultiplayerbuildspaceunitsgeneric.lua",
        "Data/Scripts/Ai/Spacemode/Spaceartillery.lua",
        "Data/Scripts/Ai/Spacemode/Hidesurpriseunits.lua"
    ];

    private readonly EawLspServerFixture _fixture;

    public RenameRoundtripSmokeTest(EawLspServerFixture fixture)
    {
        _fixture = fixture;
    }

    // ── 4 test methods ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Lua_FreshEditor_RenameXmlObject_AndBack()
    {
        RequireEawWorkspace();
        await WaitForFullScanAsync();
        // Only Interdictor.lua open - all other affected files remain closed (on disk).
        await RunRenameRoundtripAsync(InterdictorRel, true, []);
    }

    [Fact]
    public async Task Lua_CachedTabs_RenameXmlObject_AndBack()
    {
        RequireEawWorkspace();
        await WaitForFullScanAsync();
        // Simulate a user whose editor already has all relevant files open as cached tabs.
        await RunRenameRoundtripAsync(InterdictorRel, true,
            [.. KnownLuaFiles, CorvettesXmlRel]);
    }

    [Fact]
    public async Task Xml_FreshEditor_RenameXmlObject_AndBack()
    {
        RequireEawWorkspace();
        await WaitForFullScanAsync();
        // Only Spaceunitscorvettes.xml open - all Lua files remain closed.
        await RunRenameRoundtripAsync(CorvettesXmlRel, false, []);
    }

    [Fact]
    public async Task Xml_CachedTabs_RenameXmlObject_AndBack()
    {
        RequireEawWorkspace();
        await WaitForFullScanAsync();
        // Simulate a user with all relevant files already open.
        await RunRenameRoundtripAsync(CorvettesXmlRel, false,
            [.. KnownLuaFiles, CorvettesXmlRel]);
    }

    // ── core roundtrip logic ───────────────────────────────────────────────────

    private async Task RunRenameRoundtripAsync(
        string entryRel, bool isLuaEntry, string[] preOpenRels)
    {
        var workspace = LspTestEnvironment.EawWorkspacePath!;
        var entryPath = Path.Combine(workspace, entryRel);
        var entryUri = DocumentUri.FromFileSystemPath(entryPath);

        // In-memory content and version numbers (OrdinalIgnoreCase - URIs are normalised).
        var inMemory = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var versions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Build the open set: entry file always included; deduplicate.
        var toOpen = preOpenRels.Append(entryRel).Distinct().ToArray();
        var renamed = false;

        try
        {
            // ── Open files ─────────────────────────────────────────────────────
            foreach (var rel in toOpen)
            {
                var path = Path.Combine(workspace, rel);
                var uri = DocumentUri.FromFileSystemPath(path);
                var text = await File.ReadAllTextAsync(path);
                var uriStr = uri.ToString();
                inMemory[uriStr] = text;
                versions[uriStr] = 1;
                _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
                {
                    TextDocument = new TextDocumentItem
                    {
                        Uri = uri,
                        LanguageId = rel.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? "lua" : "xml",
                        Version = 1,
                        Text = text
                    }
                });
            }

            await Task.Delay(300);

            // ── Find cursor inside the symbol ──────────────────────────────────
            var entryLines = inMemory[entryUri.ToString()].Split('\n');
            var (cursorLine, cursorCol) = isLuaEntry
                ? FindLuaStringArgPosition(entryLines, "Find_Object_Type", OriginalName)
                : FindXmlNameAttributeValuePosition(entryLines, "SpaceUnit", "Name", OriginalName);

            Assert.True(cursorLine >= 0,
                $"Could not find '{OriginalName}' in {entryRel}");

            // ── PrepareRename: pre-fill range must span the full symbol name ───
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var prepared = await _fixture.Client.PrepareRename(
                new PrepareRenameParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = entryUri },
                    Position = new Position(cursorLine, cursorCol)
                }, cts1.Token);

            Assert.NotNull(prepared);
            Assert.NotNull(prepared!.Range);
            Assert.Equal(OriginalName.Length,
                prepared.Range!.End.Character - prepared.Range.Start.Character);

            // ── Rename → "Rename_Test" ─────────────────────────────────────────
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var edit = await _fixture.Client.RequestRename(
                new RenameParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = entryUri },
                    Position = new Position(cursorLine, cursorCol),
                    NewName = NewName
                }, cts2.Token);

            Assert.NotNull(edit);
            Assert.NotNull(edit!.Changes);

            var changedUris = edit.Changes!.Keys.Select(u => u.ToString()).ToList();

            Assert.True(
                changedUris.Any(u => u.Contains("Spaceunitscorvettes", StringComparison.OrdinalIgnoreCase)),
                $"WorkspaceEdit must contain the XML definition file (Spaceunitscorvettes.xml). Got: {string.Join(", ", changedUris)}");

            var luaFileCount = changedUris.Count(u => u.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
            Assert.True(luaFileCount >= 2,
                $"Expected ≥ 2 Lua files in WorkspaceEdit but got {luaFileCount}. URIs: {string.Join(", ", changedUris)}");

            var totalEdits = edit.Changes.Sum(kvp => kvp.Value.Count());
            Assert.True(totalEdits >= 3,
                $"Expected ≥ 3 total TextEdits but got {totalEdits}");

            // ── Subscribe to corvettes diagnostics, apply edits ────────────────
            var corvettesUri = DocumentUri.FromFileSystemPath(Path.Combine(workspace, CorvettesXmlRel));
            var diagTask = _fixture.WaitForDiagnosticsAsync(corvettesUri, TimeSpan.FromSeconds(15));

            await ApplyWorkspaceEditAsync(edit, inMemory, versions);
            renamed = true;

            var diags = await diagTask;
            var diagMessages = diags.Diagnostics?.Select(d => d.Message).ToList() ?? [];

            // "Rename_Test" must not be unresolved (proves symbol was properly defined/indexed).
            Assert.False(
                diagMessages.Any(m =>
                    m.Contains(NewName, StringComparison.OrdinalIgnoreCase) &&
                    m.Contains("Cannot resolve", StringComparison.OrdinalIgnoreCase)),
                $"'{NewName}' should not be unresolved after rename. Got: {string.Join("; ", diagMessages.Where(m => m.Contains(NewName, StringComparison.OrdinalIgnoreCase)))}");

            // Old name must not appear in diagnostics (proves no stale references in corvettes.xml).
            Assert.False(
                diagMessages.Any(m => m.Contains(OriginalName, StringComparison.OrdinalIgnoreCase)),
                $"Old name '{OriginalName}' still appears in corvettes.xml diagnostics after rename - stale diagnostics?");

            // ── Locate "Rename_Test" in updated entry file for rename-back ─────
            var updatedLines = inMemory[entryUri.ToString()].Split('\n');
            var (backLine, backCol) = isLuaEntry
                ? FindLuaStringArgPosition(updatedLines, "Find_Object_Type", NewName)
                : FindXmlNameAttributeValuePosition(updatedLines, "SpaceUnit", "Name", NewName);

            Assert.True(backLine >= 0,
                $"Could not find '{NewName}' in updated {entryRel} - entry file was not updated");

            // ── PrepareRename back ─────────────────────────────────────────────
            using var cts3 = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var preparedBack = await _fixture.Client.PrepareRename(
                new PrepareRenameParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = entryUri },
                    Position = new Position(backLine, backCol)
                }, cts3.Token);

            Assert.NotNull(preparedBack);
            Assert.NotNull(preparedBack!.Range);
            Assert.Equal(NewName.Length,
                preparedBack.Range!.End.Character - preparedBack.Range.Start.Character);

            // ── Rename back → "Marauder_Missile_Cruiser" ──────────────────────
            using var cts4 = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var editBack = await _fixture.Client.RequestRename(
                new RenameParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = entryUri },
                    Position = new Position(backLine, backCol),
                    NewName = OriginalName
                }, cts4.Token);

            Assert.NotNull(editBack);
            Assert.NotNull(editBack!.Changes);

            var backUris = editBack.Changes!.Keys.Select(u => u.ToString()).ToList();
            Assert.True(
                backUris.Any(u => u.Contains("Spaceunitscorvettes", StringComparison.OrdinalIgnoreCase)),
                $"Rename-back WorkspaceEdit must contain Spaceunitscorvettes.xml. Got: {string.Join(", ", backUris)}");
            Assert.True(
                backUris.Count(u => u.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) >= 2,
                $"Rename-back expected ≥ 2 Lua files. Got: {string.Join(", ", backUris)}");

            // ── Apply rename-back, verify diagnostics are clean ───────────────
            var diagBackTask = _fixture.WaitForDiagnosticsAsync(corvettesUri, TimeSpan.FromSeconds(15));
            await ApplyWorkspaceEditAsync(editBack, inMemory, versions);
            renamed = false;

            var diagsBack = await diagBackTask;
            var backMessages = diagsBack.Diagnostics?.Select(d => d.Message).ToList() ?? [];

            Assert.False(
                backMessages.Any(m =>
                    m.Contains(OriginalName, StringComparison.OrdinalIgnoreCase) &&
                    m.Contains("Cannot resolve", StringComparison.OrdinalIgnoreCase)),
                $"'{OriginalName}' should be resolved after rename-back. Got: {string.Join("; ", backMessages.Where(m => m.Contains(OriginalName, StringComparison.OrdinalIgnoreCase)))}");

            Assert.False(
                backMessages.Any(m =>
                    m.Contains(NewName, StringComparison.OrdinalIgnoreCase)),
                $"'{NewName}' still appears in corvettes.xml diagnostics after rename-back - stale?");
        }
        finally
        {
            // If the forward rename was applied but the back-rename was not (test failed mid-way),
            // restore all modified files from disk so subsequent tests start from a clean state.
            if (renamed)
            {
                var restoreVersion = versions.Values.DefaultIfEmpty(0).Max() + 100;
                foreach (var uriStr in inMemory.Keys)
                {
                    var uri = DocumentUri.From(uriStr);
                    var path = uri.GetFileSystemPath();
                    if (path is null || !File.Exists(path)) continue;
                    var diskContent = await File.ReadAllTextAsync(path);
                    _fixture.Client.DidChangeTextDocument(new DidChangeTextDocumentParams
                    {
                        TextDocument = new OptionalVersionedTextDocumentIdentifier
                        {
                            Uri = uri,
                            Version = restoreVersion
                        },
                        ContentChanges = new Container<TextDocumentContentChangeEvent>(
                            new TextDocumentContentChangeEvent { Text = diskContent })
                    });
                }

                await Task.Delay(200);
            }

            // Always close every file the test opened.
            foreach (var uriStr in inMemory.Keys)
                _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = DocumentUri.From(uriStr) }
                });
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private async Task ApplyWorkspaceEditAsync(
        WorkspaceEdit edit,
        Dictionary<string, string> inMemory,
        Dictionary<string, int> versions)
    {
        foreach (var (docUri, textEdits) in edit.Changes!)
        {
            var uriStr = docUri.ToString();
            var path = docUri.GetFileSystemPath();

            string current;
            if (inMemory.TryGetValue(uriStr, out var cached))
                current = cached;
            else
                current = path is not null && File.Exists(path)
                    ? await File.ReadAllTextAsync(path)
                    : string.Empty;

            var updated = ApplyTextEdits(current, textEdits);
            inMemory[uriStr] = updated;

            var ext = Path.GetExtension(path ?? "");
            var lang = ext.Equals(".lua", StringComparison.OrdinalIgnoreCase) ? "lua" : "xml";

            if (!versions.TryGetValue(uriStr, out var ver))
            {
                versions[uriStr] = 1;
                _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
                {
                    TextDocument = new TextDocumentItem
                    {
                        Uri = docUri,
                        LanguageId = lang,
                        Version = 1,
                        Text = updated
                    }
                });
            }
            else
            {
                versions[uriStr] = ver + 1;
                _fixture.Client.DidChangeTextDocument(new DidChangeTextDocumentParams
                {
                    TextDocument = new OptionalVersionedTextDocumentIdentifier
                    {
                        Uri = docUri,
                        Version = versions[uriStr]
                    },
                    ContentChanges = new Container<TextDocumentContentChangeEvent>(
                        new TextDocumentContentChangeEvent { Text = updated })
                });
            }
        }

        await Task.Delay(200);
    }

    private static string ApplyTextEdits(string text, IEnumerable<TextEdit> edits)
    {
        var lines = text.Split('\n').ToList();
        foreach (var edit in edits.OrderByDescending(e => (e.Range.Start.Line, e.Range.Start.Character)))
        {
            var line = lines[edit.Range.Start.Line];
            lines[edit.Range.Start.Line] =
                line[..edit.Range.Start.Character]
                + edit.NewText
                + line[edit.Range.End.Character..];
        }

        return string.Join('\n', lines);
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
    ///     Returns the 0-based (line, column) of the first character of <paramref name="value" />
    ///     inside an XML attribute like <c>&lt;tagName attrName="value"</c>.
    /// </summary>
    private static (int line, int col) FindXmlNameAttributeValuePosition(
        string[] lines, string tagName, string attrName, string value)
    {
        var tagOpen = $"<{tagName}";
        var attr = $"{attrName}=\"{value}\"";
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains(tagOpen, StringComparison.OrdinalIgnoreCase)) continue;
            var attrIdx = lines[i].IndexOf(attr, StringComparison.OrdinalIgnoreCase);
            if (attrIdx < 0) continue;
            return (i, attrIdx + attrName.Length + 2); // +2 for '=' and '"'
        }

        return (-1, -1);
    }

    private static void RequireEawWorkspace()
    {
        if (LspTestEnvironment.EawWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception(
                "$XunitDynamicSkip$eaw/ workspace or schema/eaw/ not found - cannot run rename roundtrip tests.");
    }

    private async Task WaitForFullScanAsync()
    {
        // Wait for $/workspaceScanComplete, not just the first diagnostic notification.
        var completed = await Task.WhenAny(_fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(180)));
        if (completed != _fixture.ScanCompleted)
            throw new Exception(
                "$XunitDynamicSkip$Workspace scan did not complete within 180 s.");
    }
}