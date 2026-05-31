// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.E2E.Tests;

/// <summary>
///     E2E smoke tests for reference-validation correctness.
///     These guard against false-positive diagnostics on structurally valid XML.
/// </summary>
[Trait("Category", "E2E")]
public sealed class XmlReferenceValidationSmokeTest : IClassFixture<LspServerFixture>
{
    private readonly LspServerFixture _fixture;

    public XmlReferenceValidationSmokeTest(LspServerFixture fixture)
    {
        _fixture = fixture;
    }

    // ── Squadron_Units (GameObjectTypeReferenceList / GameObjectType) ─────────

    [Fact]
    public async Task Squadron_Units_produces_no_TypeMismatch_diagnostic()
    {
        RequireSchema();

        var diags = await OpenAndCollectDiagnosticsAsync("reference_validation.xml");

        var typeMismatches = TypeMismatchDiagnostics(diags);
        Assert.Empty(typeMismatches);
    }

    // ── Encyclopedia_Good_Against (TypeReferenceList / GameObjectType) ────────

    [Fact]
    public async Task Encyclopedia_Good_Against_produces_no_TypeMismatch_diagnostic()
    {
        RequireSchema();

        var diags = await OpenAndCollectDiagnosticsAsync("reference_validation.xml");

        var typeMismatches = TypeMismatchDiagnostics(diags);
        Assert.Empty(typeMismatches);
    }

    // ── Unresolved references ARE expected (separate concern) ─────────────────

    [Fact]
    public async Task Reference_validation_file_produces_only_UnresolvedReference_not_TypeMismatch()
    {
        RequireSchema();

        var diags = await OpenAndCollectDiagnosticsAsync("reference_validation.xml");

        // Synthetic names (LSP_TEST_*) don't exist in the workspace — unresolved errors are expected.
        var unresolved = diags.Where(d =>
            d.Message.Contains("Cannot resolve reference", StringComparison.OrdinalIgnoreCase)).ToList();

        var typeMismatches = TypeMismatchDiagnostics(diags);

        // At least some unresolved errors expected; zero type mismatches required.
        Assert.Empty(typeMismatches);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void RequireSchema()
    {
        if (LspTestEnvironment.SchemaLocalPath is null)
            throw new Exception("$XunitDynamicSkip$" +
                                "Schema not found. Ensure schema/eaw/ submodule is checked out or set LSP_SCHEMA_LOCAL_PATH.");
    }

    private async Task<IReadOnlyList<Diagnostic>> OpenAndCollectDiagnosticsAsync(string fileName)
    {
        var filePath = Path.Combine(_fixture.TestDataDirectory, fileName);
        var uri = DocumentUri.FromFileSystemPath(filePath);
        var text = await File.ReadAllTextAsync(filePath);

        // Subscribe before DidOpen so we don't miss the notification.
        var diagsTask = WaitForFinalDiagnosticsAsync(uri, TimeSpan.FromSeconds(15));

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

        var result = await diagsTask;
        return result?.Diagnostics?.ToList() ?? [];
    }

    private static IReadOnlyList<Diagnostic> TypeMismatchDiagnostics(IReadOnlyList<Diagnostic> diags)
    {
        return diags.Where(d =>
            d.Message.Contains("Type mismatch", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    ///     Waits for diagnostics for the given URI, then keeps replacing the result with any
    ///     subsequent publish that arrives within a short quiet window.  This captures the
    ///     index-level publish that follows the initial document-level one.
    /// </summary>
    private Task<PublishDiagnosticsParams?> WaitForFinalDiagnosticsAsync(DocumentUri uri, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<PublishDiagnosticsParams?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var uriStr = uri.ToString();

        PublishDiagnosticsParams? last = null;
        CancellationTokenSource? quietTimer = null;

        void OnDiagnostics(PublishDiagnosticsParams p)
        {
            if (!string.Equals(p.Uri.ToString(), uriStr, StringComparison.OrdinalIgnoreCase)) return;
            last = p;

            quietTimer?.Cancel();
            quietTimer = new CancellationTokenSource();
            var token = quietTimer.Token;
            _ = Task.Delay(TimeSpan.FromSeconds(1), token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    _fixture.DiagnosticsReceived -= OnDiagnostics;
                    tcs.TrySetResult(last);
                }
            }, TaskScheduler.Default);
        }

        _fixture.DiagnosticsReceived += OnDiagnostics;

        _ = Task.Delay(timeout).ContinueWith(_ =>
        {
            _fixture.DiagnosticsReceived -= OnDiagnostics;
            quietTimer?.Cancel();
            tcs.TrySetResult(last);
        }, TaskScheduler.Default);

        return tcs.Task;
    }
}