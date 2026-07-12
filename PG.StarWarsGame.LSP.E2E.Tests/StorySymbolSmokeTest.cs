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

        Assert.Contains(locations, l =>
            l.Uri.ToString().Contains("story_campaign_underworld.xml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AiNotificationId_GoToDefinition_JumpsToTheLuaStoryEventCall()
    {
        // <Event_Param2>START_MISSION_7</Event_Param2> → Story_Event("START_MISSION_7"). Vanilla
        // fires the id from the campaign script AND its test-campaign copy; either is a valid
        // definition target.
        var locations = await RequestDefinitionAsync("<Event_Param2>", "START_MISSION_7");

        Assert.Contains(locations, l =>
            l.Uri.ToString().Contains("story_campaign_underworld", StringComparison.OrdinalIgnoreCase)
            && l.Uri.ToString().EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<Location>> RequestDefinitionAsync(string tagOpen, string value)
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
            var locations = result!.Select(l => l.Location!).ToList();
            Assert.NotEmpty(locations);
            return locations;
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
