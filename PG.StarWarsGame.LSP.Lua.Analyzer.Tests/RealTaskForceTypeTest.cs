// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Lua.Analyzer.Tests;

/// <summary>
///     A plan's task forces are globals of type TaskForce: the engine creates one per entry of the
///     plan's TaskForce table. The analyzer sees no assignment, so the type is generated for it.
/// </summary>
/// <remarks>Local only, see <see cref="VanillaScriptStack" />.</remarks>
public sealed class RealTaskForceTypeTest
{
    private const string Plan = "Ai/Raidplan.lua";

    [Fact]
    public async Task APlansTaskForce_IsATaskForce()
    {
        await using var stack = VanillaScriptStack.Create(Plan);
        var script = await OpenAsync(stack);

        // "	if not RaidForce.Is_Raid_Capable() then", line 70
        var hover = await stack.Analyzer.RequestAsync<Hover>("textDocument/hover", new HoverParams
        {
            TextDocument = new TextDocumentIdentifier(DocumentUri.From(script)),
            Position = new Position(69, 10)
        }, TestContext.Current.CancellationToken);

        Assert.NotNull(hover);
        Assert.Contains("TaskForce", hover.Contents.MarkupContent?.Value ?? hover.Contents.ToString());
    }

    [Fact]
    public async Task APlansTaskForce_CompletesTheTaskForceMethods()
    {
        await using var stack = VanillaScriptStack.Create(Plan);
        var script = await OpenAsync(stack);

        // After "RaidForce." on "	if not RaidForce.Is_Raid_Capable() then", line 70
        var completion = await stack.Analyzer.RequestAsync<CompletionList>("textDocument/completion",
            new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier(DocumentUri.From(script)),
                Position = new Position(69, 18)
            }, TestContext.Current.CancellationToken);

        Assert.Contains(completion?.Items ?? [], i => i.Label == "Produce_Force");
    }

    // The generated declaration assigns nil: the declared type has to win, or every call on the
    // force is a possible nil.
    [Fact]
    public async Task APlansTaskForce_RaisesNoFinding()
    {
        await using var stack = VanillaScriptStack.Create(Plan);
        var script = stack.UriOf(Plan);
        PublishDiagnosticsParams? published = null;
        stack.Analyzer.DiagnosticsPublished += p =>
        {
            if (DocumentUris.Same(p.Uri.ToString(), script)) published = p;
        };
        stack.Analyzer.DidOpen(script, await File.ReadAllTextAsync(stack.FileHelper.FileUriToPath(script)!,
            TestContext.Current.CancellationToken), 1);
        await VanillaScriptStack.UntilAsync(() => published is not null, TimeSpan.FromSeconds(30));

        Assert.NotNull(published);
        var findings = published.Diagnostics.Select(d => $"{d.Range.Start.Line + 1}: {d.Code?.String} {d.Message}")
            .ToList();
        TestContext.Current.TestOutputHelper?.WriteLine(string.Join("\n", findings));
        Assert.DoesNotContain(findings, f => f.Contains("RaidForce", StringComparison.Ordinal));
    }

    private static async Task<string> OpenAsync(VanillaScriptStack stack)
    {
        var script = stack.UriOf(Plan);
        var reported = false;
        stack.Analyzer.DiagnosticsPublished += p => reported |= DocumentUris.Same(p.Uri.ToString(), script);
        stack.Analyzer.DidOpen(script, await File.ReadAllTextAsync(stack.FileHelper.FileUriToPath(script)!,
            TestContext.Current.CancellationToken), 1);
        await VanillaScriptStack.UntilAsync(() => reported, TimeSpan.FromSeconds(30));
        return script;
    }
}