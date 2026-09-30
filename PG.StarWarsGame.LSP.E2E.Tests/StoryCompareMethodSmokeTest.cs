// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     A story flag comparison that the engine accepts and then ignores, reported end to end.
///     <para>
///         <c>NOT_EQUAL_TO</c> is a real member of <c>StoryFlagCompareMethod</c>, so nothing in the
///         file looks wrong: it parses, it validates, and the event simply never fires. The schema
///         carries that as a <c>BuggedInEngine</c> note and this is the proof the note reaches an
///         author as an error rather than dying in a unit test.
///     </para>
///     <para>
///         It runs against <c>e2e-workspace/</c> because the shipped corpus contains no use of the
///         value - nobody writes an operator that never worked - and because the story chain has to
///         be real: a thread file is story content only if a plot manifest names it, the manifest is
///         read only if a campaign names it, and the campaign is read only if the Campaignfiles
///         metafile lists it. Every one of those links fails silently on its own.
///     </para>
/// </summary>
[Trait("Category", "E2E")]
public sealed class StoryCompareMethodSmokeTest : IClassFixture<E2eModServerFixture>
{
    private const string Fixture = "Story_E2E_Compare.xml";

    private readonly E2eModServerFixture _fixture;

    public StoryCompareMethodSmokeTest(E2eModServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BuggedCompareMethod_IsReportedAsAnError_AndSaysWhy()
    {
        var diagnostics = await DiagnosticsAsync();

        var bugged = diagnostics
            .Where(d => d.Message.Contains("NOT_EQUAL_TO", StringComparison.Ordinal))
            .ToList();

        Assert.True(bugged.Count > 0,
            "Nothing reported the bugged operator. Everything on the file: "
            + (diagnostics.Count == 0
                ? "(none)"
                : string.Join(" | ", diagnostics.Select(d => $"[{d.Severity}] {d.Code?.String} {d.Message}"))));
        Assert.Contains(bugged, d => d.Severity == DiagnosticSeverity.Error);

        // The value alone is not actionable - an author who wanted "not equal" needs to be told what
        // the engine does with it, and that there is another way to write the condition.
        Assert.Contains(bugged, d =>
            d.Message.Contains("never fires", StringComparison.OrdinalIgnoreCase)
            || d.Message.Contains("different operator", StringComparison.OrdinalIgnoreCase));

        // Suppressible by id like every other diagnostic, or an author who disagrees has no way out.
        Assert.Contains(bugged, d => d.Code?.String is { Length: > 0 });
    }

    // Without this the test would pass just as well against a check that flagged every compare
    // method, which would be worse than no check at all.
    [Fact]
    public async Task TheWorkingCompareMethodBesideIt_IsNotReported()
    {
        var diagnostics = await DiagnosticsAsync();

        Assert.DoesNotContain(diagnostics,
            d => d.Message.Contains("GREATER_THAN_EQUAL_TO", StringComparison.Ordinal));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    // Opened once for the whole class. Re-opening the same document with the same text is a no-op
    // on the server - it logs "content unchanged" and publishes nothing - so a second test that
    // opened it again would wait for a publish that is never coming and fail as a timeout.
    private static readonly SemaphoreSlim OpenOnce = new(1, 1);
    private static Task<IReadOnlyList<Diagnostic>>? _opened;

    private async Task<IReadOnlyList<Diagnostic>> DiagnosticsAsync()
    {
        await OpenOnce.WaitAsync();
        try
        {
            return await (_opened ??= OpenFixtureAsync());
        }
        finally
        {
            OpenOnce.Release();
        }
    }

    private async Task<IReadOnlyList<Diagnostic>> OpenFixtureAsync()
    {
        if (LspTestEnvironment.E2eWorkspacePath is null || LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$e2e-workspace/ not found; cannot run story compare smoke tests.");

        var completed = await Task.WhenAny(_fixture.ScanCompleted, Task.Delay(TimeSpan.FromSeconds(60)));
        if (completed != _fixture.ScanCompleted)
            throw new Exception("$XunitDynamicSkip$E2E workspace scan did not complete within 60 s.");

        var filePath = Path.Combine(LspTestEnvironment.E2eWorkspacePath, "Data", "Xml", Fixture);
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var text = await File.ReadAllTextAsync(filePath);

        var received = _fixture.WaitForDiagnosticsAsync(uri, TimeSpan.FromSeconds(60));
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

        return (await received).Diagnostics.ToList();
    }
}