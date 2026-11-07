// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     The load order end to end, over the real schema and baseline: which shipped objects a mod's own
///     registry loads, which definition of a name the game keeps, and what the dropped one is told.
/// </summary>
[Trait("Category", "E2E")]
public sealed class LoadOrderSmokeTest(E2eLoadOrderServerFixture fixture) : IClassFixture<E2eLoadOrderServerFixture>
{
    private const string Unresolved = "aetswg-001-0021";
    private const string NotUsed = "aetswg-010-0006";
    private const string Merged = "aetswg-010-0007";

    private async Task<(List<Diagnostic> Diagnostics, string[] Lines)> OpenAsync(string file)
    {
        if (fixture.ModPath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception(
                "$XunitDynamicSkip$e2e-workspace-load-order/ not found; cannot run the load-order test.");

        // A file opened before the scan finishes is diagnosed against a partial index.
        if (await Task.WhenAny(fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(60))) != fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$The load-order workspace scan did not complete within 60 s.");

        var filePath = Path.Combine(fixture.ModPath, "Data", "XML", file);
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var text = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken);
        var settled = fixture.WaitForSettledDiagnosticsAsync(uri, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));

        fixture.Client.DidOpenTextDocument(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, LanguageId = "xml", Version = 1, Text = text }
        });

        var published = await settled;
        return ([.. published?.Diagnostics ?? []], text.Split('\n'));
    }

    // One open per file: a second open of a file already open publishes nothing new.
    //
    // A redefined shipped object and a repeat in one file are not used. Each case's reference: a
    // shipped file the mod lists, the mod's own copy, a shipped file that copy replaces, and shipped
    // files the mod's list leaves out - only the files the mod's registry loads resolve.
    [Fact]
    public async Task TheAppendedFile_LosesItsRedefinitions_AndResolvesOnlyWhatTheRegistryLoads()
    {
        var (diagnostics, lines) = await OpenAsync("E2E_LoadOrder_Appended.xml");

        var notUsed = diagnostics.Where(d => d.Code?.String == NotUsed).ToList();
        Assert.Equal(2, notUsed.Count);
        Assert.Contains(notUsed, d => lines[d.Range.Start.Line].Contains("Name=\"X-Wing\""));
        Assert.Contains(notUsed, d => lines[d.Range.Start.Line].Contains("E2E_LoadOrder_Twice") &&
                                      lines[d.Range.Start.Line + 1].Contains(">2<"));

        var unresolved = diagnostics.Where(d => d.Code?.String == Unresolved).Select(d => d.Message).ToList();
        Assert.Contains(unresolved, m => m.Contains("Scout_Trooper"));
        Assert.Contains(unresolved, m => m.Contains("Executor_Super_Star_Destroyer"));
        Assert.Contains(unresolved, m => m.Contains("Bossk"));
        Assert.DoesNotContain(unresolved, m => m.Contains("'X-Wing'") || m.Contains("E2E_LoadOrder_Copy_Trooper"));
    }

    [Fact]
    public async Task ASecondTargetingSetOfOneName_IsMerged()
    {
        var (diagnostics, lines) = await OpenAsync("E2E_Targeting_B.xml");

        var merged = Assert.Single(diagnostics, d => d.Code?.String == Merged);
        Assert.Equal(DiagnosticSeverity.Information, merged.Severity);
        Assert.Contains("E2E_LoadOrder_Set", lines[merged.Range.Start.Line]);
    }
}