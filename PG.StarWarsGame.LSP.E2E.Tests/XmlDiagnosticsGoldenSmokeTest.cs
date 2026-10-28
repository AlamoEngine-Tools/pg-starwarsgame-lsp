// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Xml.Commands;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     The diagnostics of the files the smoke tests read, pinned id for id (#307): every code, range,
///     severity and message the server publishes for them, compared against a recording under
///     <c>Golden/</c>. A refactoring of the diagnostics pipeline that changes any of them shows up
///     here, file by file, with the rows that moved.
/// </summary>
/// <remarks>
///     The recording depends on the schema and the baseline the server loads, so it is re-recorded
///     when either changes deliberately: run the test with <c>AET_GOLDEN_RECORD=1</c> in the
///     environment and it writes the files instead of comparing. The rows are compared as sets, so
///     the order the server publishes in is not part of the contract; the workspace path is scrubbed
///     out of every message, so a recording made on one machine reads on another.
/// </remarks>
public static class XmlDiagnosticsGolden
{
    public const string RecordVariable = "AET_GOLDEN_RECORD";

    // Relaxed escaping keeps a quote or an angle bracket in a message readable in the recording.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    ///     Waits for the scan, asks the server to revalidate the workspace so every file is published
    ///     once more, and checks (or records) the files named in <paramref name="relativeFiles" />.
    /// </summary>
    public static async Task CheckAsync(
        LspServerFixture fixture, string workspacePath, string goldenSet, IReadOnlyList<string> relativeFiles,
        CancellationToken ct)
    {
        var latest = new Dictionary<string, PublishDiagnosticsParams>(StringComparer.OrdinalIgnoreCase);
        var lastPublishAt = DateTime.UtcNow;

        void OnPublished(PublishDiagnosticsParams p)
        {
            lock (latest)
            {
                latest[p.Uri.ToString()] = p;
                lastPublishAt = DateTime.UtcNow;
            }
        }

        fixture.DiagnosticsReceived += OnPublished;
        try
        {
            await fixture.ScanCompleted.WaitAsync(TimeSpan.FromMinutes(3), ct);
            await fixture.Client.SendRequest(
                new ExecuteCommandParams { Command = RevalidateWorkspaceCommandHandler.CommandName }, ct);

            // The revalidation's publishes trail its response by however long the transport takes;
            // a quiet second after the last one is the end of them.
            var settleStarted = DateTime.UtcNow;
            while (DateTime.UtcNow - settleStarted < TimeSpan.FromSeconds(60))
            {
                await Task.Delay(250, ct);
                lock (latest)
                {
                    if (DateTime.UtcNow - lastPublishAt > TimeSpan.FromSeconds(1)) break;
                }
            }
        }
        finally
        {
            fixture.DiagnosticsReceived -= OnPublished;
        }

        var root = LspTestEnvironment.RepositoryRoot
                   ?? throw new InvalidOperationException("No solution above the test binaries.");
        var goldenDirectory = Path.Combine(root, "PG.StarWarsGame.LSP.E2E.Tests", "Golden", goldenSet);
        var record = Environment.GetEnvironmentVariable(RecordVariable) is { Length: > 0 };
        var failures = new List<string>();

        foreach (var relative in relativeFiles)
        {
            var path = Path.Combine(workspacePath, relative);
            var uri = DocumentUri.FromFileSystemPath(path).ToString();
            PublishDiagnosticsParams? published;
            lock (latest)
            {
                latest.TryGetValue(uri, out published);
            }

            if (published is null)
            {
                failures.Add($"{relative}: the server published nothing for {uri}");
                continue;
            }

            var rows = Rows(published, workspacePath);
            var goldenPath = Path.Combine(goldenDirectory, Path.GetFileName(relative) + ".json");
            if (record)
            {
                Directory.CreateDirectory(goldenDirectory);
                await File.WriteAllTextAsync(goldenPath, JsonSerializer.Serialize(rows, JsonOptions) + "\n", ct);
                continue;
            }

            if (!File.Exists(goldenPath))
            {
                failures.Add($"{relative}: no recording at {goldenPath}; record one with {RecordVariable}=1");
                continue;
            }

            var expected =
                JsonSerializer.Deserialize<List<Row>>(await File.ReadAllTextAsync(goldenPath, ct), JsonOptions)
                ?? [];
            var missing = expected.Except(rows).ToList();
            var unexpected = rows.Except(expected).ToList();
            if (missing.Count == 0 && unexpected.Count == 0) continue;

            failures.Add(
                $"{relative}: {missing.Count} recorded row(s) missing, {unexpected.Count} unrecorded row(s) published"
                + string.Concat(missing.Take(10).Select(r => $"\n    missing    {r}"))
                + string.Concat(unexpected.Take(10).Select(r => $"\n    unexpected {r}")));
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static List<Row> Rows(PublishDiagnosticsParams published, string workspacePath)
    {
        var workspaceUri = DocumentUri.FromFileSystemPath(workspacePath).ToString();
        var scrub = new Regex(
            string.Join("|", new[] { workspaceUri, workspacePath, workspacePath.Replace('\\', '/') }
                .Distinct().Select(Regex.Escape)),
            RegexOptions.IgnoreCase);

        return published.Diagnostics
            .Select(d => new Row(
                d.Code is { } code ? code.IsString ? code.String ?? "" : code.Long.ToString() : "",
                d.Severity?.ToString() ?? "",
                d.Range.Start.Line, d.Range.Start.Character, d.Range.End.Line, d.Range.End.Character,
                scrub.Replace(d.Message, "<ws>")))
            .OrderBy(r => r.StartLine).ThenBy(r => r.StartCharacter)
            .ThenBy(r => r.Code, StringComparer.Ordinal).ThenBy(r => r.Message, StringComparer.Ordinal)
            .ToList();
    }

    public sealed record Row(
        string Code,
        string Severity,
        int StartLine,
        int StartCharacter,
        int EndLine,
        int EndCharacter,
        string Message);
}

[Trait("Category", "E2E")]
public sealed class XmlDiagnosticsGoldenSmokeTest(LspServerFixture fixture) : IClassFixture<LspServerFixture>
{
    /// <summary>The corpus files the other smoke tests open, in the game's own folder.</summary>
    private static readonly string[] SmokeFiles =
    [
        "Data/XML/CIN_SpaceUnitsFrigates.xml",
        "Data/XML/Spaceunitsfighters.xml",
        "Data/XML/Spaceunitscapital.xml",
        "Data/XML/HardPoints.xml",
        "Data/XML/Sfxevents.xml"
    ];

    [Fact]
    public async Task TheSmokeCorpusFiles_KeepTheirDiagnostics()
    {
        if (LspTestEnvironment.WorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$Set LSP_WORKSPACE_PATH and LSP_SCHEMA_LOCAL_PATH to run this test.");

        await XmlDiagnosticsGolden.CheckAsync(fixture, LspTestEnvironment.WorkspacePath, "foc", SmokeFiles,
            TestContext.Current.CancellationToken);
    }
}

[Trait("Category", "E2E")]
public sealed class XmlDiagnosticsGoldenE2eWorkspaceTest(E2eModServerFixture fixture)
    : IClassFixture<E2eModServerFixture>
{
    /// <summary>Every XML file of the authored workspace: the story files are where the story facts live.</summary>
    private static readonly string[] WorkspaceFiles =
    [
        "Data/Xml/Campaignfiles.xml",
        "Data/Xml/Campaigns_E2E.xml",
        "Data/Xml/Groundinfantry.xml",
        "Data/Xml/Spaceunitsfighters.xml",
        "Data/Xml/Story_E2E_Compare.xml",
        "Data/Xml/Story_Plots_E2E.xml"
    ];

    [Fact]
    public async Task TheAuthoredWorkspaceFiles_KeepTheirDiagnostics()
    {
        if (LspTestEnvironment.E2eWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception(
                "$XunitDynamicSkip$Set LSP_E2E_WORKSPACE_PATH and LSP_SCHEMA_LOCAL_PATH to run this test.");

        await XmlDiagnosticsGolden.CheckAsync(fixture, LspTestEnvironment.E2eWorkspacePath, "e2e-workspace",
            WorkspaceFiles, TestContext.Current.CancellationToken);
    }
}