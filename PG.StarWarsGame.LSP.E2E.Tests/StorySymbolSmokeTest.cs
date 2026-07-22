// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     E2E smoke tests for cross-language story symbols against the vanilla foc/ workspace:
///     prereq tokens navigate to their event definition, and a STORY_AI_NOTIFICATION id
///     navigates to the Lua <c>Story_Event</c> call that fires it.
/// </summary>
[Trait("Category", "E2E")]
public sealed class StorySymbolSmokeTest : IClassFixture<LspServerFixture>
{
    private const string ThreadXmlRel = "Data/XML/Story_campaign_underworld.xml";

    private readonly LspServerFixture _fixture;

    public StorySymbolSmokeTest(LspServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PrereqToken_GoToDefinition_JumpsToTheEventDefinition()
    {
        // <Prereq>Underworld_Campaign_Begin</Prereq> → <Event Name="Underworld_Campaign_Begin">
        var locations = await RequestDefinitionAsync("<Prereq>", "Underworld_Campaign_Begin");

        Assert.Contains(locations, u =>
            u.ToString().Contains("story_campaign_underworld.xml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AiNotificationId_GoToDefinition_JumpsToTheLuaStoryEventCall()
    {
        // <Event_Param2>START_MISSION_7</Event_Param2> → Story_Event("START_MISSION_7"). Vanilla
        // fires the id from the campaign script AND its test-campaign copy; either is a valid
        // definition target.
        var locations = await RequestDefinitionAsync("<Event_Param2>", "START_MISSION_7");

        Assert.Contains(locations, u =>
            u.ToString().Contains("story_campaign_underworld", StringComparison.OrdinalIgnoreCase)
            && u.ToString().EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Regression guard for #60: multiple &lt;Prereq&gt; lines on one event are an OR of
    ///     AND-groups, not a mistake. Before <c>multipleAllowed: true</c> was set on the
    ///     StoryParser <c>Prereq</c> tag, every OR-chained event drew a spurious duplicate-tag
    ///     warning. Vanilla <c>Story_campaign_underworld.xml</c> contains several such events
    ///     (e.g. <c>Unlock_Corruption_Shola</c> with three).
    /// </summary>
    [Fact]
    public async Task MultiplePrereqLines_DoNotEmitDuplicateTagDiagnostics()
    {
        RequireWorkspace();
        await WaitForScanAsync();

        var xmlPath = Path.Combine(LspTestEnvironment.WorkspacePath!, ThreadXmlRel);
        var xmlUri = DocumentUri.FromFileSystemPath(xmlPath);
        var lines = await File.ReadAllLinesAsync(xmlPath);

        var received = _fixture.WaitForDiagnosticsAsync(xmlUri, TimeSpan.FromSeconds(20));
        _fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = xmlUri, LanguageId = "xml", Version = 1,
                Text = string.Join(Environment.NewLine, lines)
            }
        });

        try
        {
            var diagnostics = await received;
            var prereqDuplicates = diagnostics.Diagnostics
                .Where(d => d.Message.Contains("Duplicate tag 'Prereq'", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.True(prereqDuplicates.Count == 0,
                "Multiple <Prereq> lines are an OR condition and must not be reported as duplicates. Got: "
                + string.Join(" | ", prereqDuplicates.Select(d => $"line {d.Range.Start.Line + 1}: {d.Message}")));
        }
        finally
        {
            _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = xmlUri }
            });
        }
    }

    private async Task<IReadOnlyList<DocumentUri>> RequestDefinitionAsync(string tagOpen, string value)
    {
        RequireWorkspace();
        await WaitForScanAsync();

        var xmlPath = Path.Combine(LspTestEnvironment.WorkspacePath!, ThreadXmlRel);
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
            var (line, col) = FindTagValuePosition(lines, tagOpen, value);
            Assert.True(line >= 0, $"Could not find '{tagOpen}{value}' in {ThreadXmlRel}");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await _fixture.Client.RequestDefinition(
                new DefinitionParams
                {
                    TextDocument = new TextDocumentIdentifier { Uri = xmlUri },
                    Position = new Position(line, col)
                }, cts.Token);

            Assert.NotNull(result);
            // Definitions come back as LocationLinks (carrying an originSelectionRange); tolerate the
            // plain-Location shape too. Callers only assert on the target URI.
            var targetUris = result!
                .Select(l => l.IsLocationLink ? l.LocationLink!.TargetUri : l.Location!.Uri)
                .ToList();
            Assert.NotEmpty(targetUris);
            return targetUris;
        }
        finally
        {
            _fixture.Client.DidCloseTextDocument(new DidCloseTextDocumentParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = xmlUri }
            });
        }
    }

    private static (int line, int col) FindTagValuePosition(string[] lines, string tagOpen, string value)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var tagIdx = lines[i].IndexOf(tagOpen, StringComparison.OrdinalIgnoreCase);
            if (tagIdx < 0) continue;
            var valueIdx = lines[i].IndexOf(value, tagIdx + tagOpen.Length, StringComparison.Ordinal);
            if (valueIdx < 0) continue;
            return (i, valueIdx);
        }

        return (-1, -1);
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
}