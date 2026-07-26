// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Story.Dialog;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Story.Tests.Dialog;

/// <summary>
///     Suppression quick fixes for story-dialog scripts. Dialog had none at all until now, so a
///     user could honour a directive only by typing it from memory - the ids are deliberately
///     numeric, which makes them exactly the thing nobody remembers.
/// </summary>
public sealed class DialogCodeActionHandlerTest
{
    private const string Uri = "file:///ws/dialogs/dialog_test.txt";

    private static readonly DiagnosticId Unknown = DiagnosticIds.DialogUnknownCommand;

    private static DialogCodeActionHandler Handler(string text, bool inScope = true, bool flag = true)
    {
        return new DialogCodeActionHandler(
            new StubScope(inScope),
            new FileHelper(new MockFileSystem()),
            new StubTextSource(Uri, text),
            FakeLspConfigurationProvider.WithFeatures(new FeatureFlags
            {
                Dialog = new DialogFeatureFlags { CodeActions = flag }
            }));
    }

    private static CodeActionParams Request(int line, string? code)
    {
        var diagnostic = new Diagnostic
        {
            Range = new Range(line, 0, line, 5),
            Message = "unknown command",
            Code = code is null ? null : new DiagnosticCode(code)
        };

        return new CodeActionParams
        {
            TextDocument = new TextDocumentIdentifier(Uri),
            Range = diagnostic.Range,
            Context = new CodeActionContext { Diagnostics = new Container<Diagnostic>(diagnostic) }
        };
    }

    private static async Task<List<CodeAction>> Actions(
        DialogCodeActionHandler handler, CodeActionParams request)
    {
        var result = await handler.Handle(request, CancellationToken.None);
        return result!.Select(a => a.CodeAction!).ToList();
    }

    // ── the offered scopes ───────────────────────────────────────────────────

    [Fact]
    public async Task InsideAChapter_OffersLineChapterFileAndProject()
    {
        var handler = Handler("[CHAPTER 0]\nBOGUS 1\n");

        var titles = (await Actions(handler, Request(1, Unknown.ToString()))).Select(a => a.Title);

        Assert.Equal([
            $"Suppress {Unknown} for this line",
            $"Suppress {Unknown} for this chapter",
            $"Suppress {Unknown} in this file",
            $"Suppress {Unknown} across the project"
        ], titles);
    }

    // No chapter to attach it to means no chapter action - offering one that silently covered the
    // whole file instead would suppress more than the user asked for.
    [Fact]
    public async Task OutsideAnyChapter_OmitsTheChapterScope()
    {
        var handler = Handler("BOGUS 1\n");

        var titles = (await Actions(handler, Request(0, Unknown.ToString()))).Select(a => a.Title);

        Assert.DoesNotContain($"Suppress {Unknown} for this chapter", titles);
        Assert.Equal(3, titles.Count());
    }

    // ── the edits ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LineScope_InsertsAHashDirectiveAboveTheDiagnostic()
    {
        var handler = Handler("[CHAPTER 0]\n  BOGUS 1\n");

        var action = (await Actions(handler, Request(1, Unknown.ToString()))).First();
        var edit = Assert.Single(action.Edit!.Changes![new DocumentUri("file", "", "/ws/dialogs/dialog_test.txt", "", "")]);

        // Indented to match the line it guards, so the file needs no reformatting afterwards.
        Assert.Equal($"  # aetswg:suppress {Unknown}\n", edit.NewText);
        Assert.Equal(1, edit.Range.Start.Line);
        Assert.Equal(0, edit.Range.Start.Character);
    }

    [Fact]
    public async Task ChapterScope_InsertsJustBelowTheChapterHeader()
    {
        var handler = Handler("[CHAPTER 0]\nWAIT 500\nBOGUS 1\n");

        var actions = await Actions(handler, Request(2, Unknown.ToString()));
        var chapter = actions.Single(a => a.Title.EndsWith("for this chapter", StringComparison.Ordinal));
        var edit = chapter.Edit!.Changes!.Values.Single().Single();

        Assert.Equal(1, edit.Range.Start.Line);
        Assert.Contains("aetswg:suppress-object", edit.NewText, StringComparison.Ordinal);
    }

    // Project scope writes .aetswg/suppressions.json, which is the server's job, not a text edit.
    [Fact]
    public async Task ProjectScope_IsACommandRatherThanAnEdit()
    {
        var handler = Handler("[CHAPTER 0]\nBOGUS 1\n");

        var action = (await Actions(handler, Request(1, Unknown.ToString()))).Last();

        Assert.Null(action.Edit);
        Assert.Equal(SuppressionCommands.SuppressGlobally, action.Command!.Name);
    }

    // Silencing a diagnostic should never be the fix a user lands on by reflex.
    [Fact]
    public async Task NoSuppressionAction_IsMarkedPreferred()
    {
        var handler = Handler("[CHAPTER 0]\nBOGUS 1\n");

        var actions = await Actions(handler, Request(1, Unknown.ToString()));

        Assert.All(actions, a => Assert.NotEqual(true, a.IsPreferred));
    }

    // ── when nothing is offered ──────────────────────────────────────────────

    [Fact]
    public async Task DiagnosticWithoutAnId_OffersNothing()
    {
        var handler = Handler("[CHAPTER 0]\nBOGUS 1\n");

        Assert.Empty(await Actions(handler, Request(1, null)));
    }

    [Fact]
    public async Task DiagnosticFromAnotherSource_OffersNothing()
    {
        var handler = Handler("[CHAPTER 0]\nBOGUS 1\n");

        Assert.Empty(await Actions(handler, Request(1, "some-other-tool-code")));
    }

    [Fact]
    public async Task OutOfScopeDocument_OffersNothing()
    {
        var handler = Handler("[CHAPTER 0]\nBOGUS 1\n", false);

        Assert.Empty(await Actions(handler, Request(1, Unknown.ToString())));
    }

    [Fact]
    public async Task FlagOff_OffersNothing()
    {
        var handler = Handler("[CHAPTER 0]\nBOGUS 1\n", flag: false);

        Assert.Empty(await Actions(handler, Request(1, Unknown.ToString())));
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class StubScope(bool inScope) : IStoryDialogScope
    {
        public bool Enabled => inScope;

        public bool IsInScope(string canonicalUri)
        {
            return inScope;
        }

        public string? ResolveDialogFile(string dialogName)
        {
            return null;
        }

        public IReadOnlyCollection<int> GetChapters(string canonicalUri)
        {
            return [];
        }
    }

    private sealed class StubTextSource(string uri, string text) : IDocumentTextSource
    {
        public DocumentText? GetText(string canonicalUri)
        {
            return string.Equals(canonicalUri, uri, StringComparison.OrdinalIgnoreCase)
                ? new DocumentText(text, 0, true)
                : null;
        }
    }
}
