// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Story.Dialog;

/// <summary>
///     Suppression quick fixes for story-dialog scripts: the only code actions dialog has, and the
///     reason it needed a handler at all. Directives could always be typed by hand, but the ids are
///     numeric by design, so without a fix that writes them the feature is only usable by someone
///     willing to copy an id out of the Problems panel.
///     <para>
///         The actions themselves come from <see cref="SuppressionCodeActionBuilder" />, shared with
///         XML and Lua. Dialog supplies only where its scopes anchor: the reported line, the
///         enclosing <c>[CHAPTER n]</c> section, and the top of the file.
///     </para>
///     <para>
///         Scope-gated like every other dialog handler: a .txt outside the pgproj storyDialog
///         directories is not a dialog script and gets nothing.
///     </para>
/// </summary>
public sealed class DialogCodeActionHandler : CodeActionHandlerBase
{
    private readonly ILspConfigurationProvider _config;
    private readonly IFileHelper _fileHelper;
    private readonly IStoryDialogScope _scope;
    private readonly IDocumentTextSource _textSource;

    public DialogCodeActionHandler(
        IStoryDialogScope scope,
        IFileHelper fileHelper,
        IDocumentTextSource textSource,
        ILspConfigurationProvider config)
    {
        _scope = scope;
        _fileHelper = fileHelper;
        _textSource = textSource;
        _config = config;
    }

    public override Task<CommandOrCodeActionContainer?> Handle(
        CodeActionParams request, CancellationToken ct)
    {
        return Task.FromResult<CommandOrCodeActionContainer?>(
            new CommandOrCodeActionContainer(Build(request)));
    }

    public override Task<CodeAction> Handle(CodeAction request, CancellationToken ct)
    {
        return Task.FromResult(request);
    }

    protected override CodeActionRegistrationOptions CreateRegistrationOptions(
        CodeActionCapability capability, ClientCapabilities clientCapabilities)
    {
        return new CodeActionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.txt"),
            CodeActionKinds = new Container<CodeActionKind>(CodeActionKind.QuickFix),
            ResolveProvider = false
        };
    }

    private IEnumerable<CommandOrCodeAction> Build(CodeActionParams request)
    {
        if (!_config.Current.Features.Dialog.CodeActions) return [];

        var uri = _fileHelper.NormalizeUri(request.TextDocument.Uri.ToString());
        if (!_scope.IsInScope(uri)) return [];

        var text = _textSource.GetText(uri)?.Text;
        if (text is null) return [];

        var lines = text.Split('\n');
        var document = StoryDialogParser.Parse(text);

        return request.Context.Diagnostics
            .Where(d => DiagnosticId.TryParse(d.Code?.String, out _))
            .SelectMany(d => ActionsFor(request.TextDocument.Uri, d, document, lines))
            .ToList();
    }

    private static IEnumerable<CommandOrCodeAction> ActionsFor(
        DocumentUri documentUri, Diagnostic diagnostic, StoryDialogDocument document, string[] lines)
    {
        DiagnosticId.TryParse(diagnostic.Code?.String, out var id);

        var line = diagnostic.Range.Start.Line;
        var points = new List<SuppressionInsertionPoint> { new(SuppressionScope.Node, line) };

        // Offered only when there is a chapter to attach it to. A diagnostic before the first
        // header has no section, and an -object directive there would collapse onto its own line -
        // an action whose title promised the chapter and delivered one line.
        if (DialogSuppressionCommentParser.ChapterHeaderLine(document, line) is { } header)
            points.Add(new SuppressionInsertionPoint(SuppressionScope.Object, header + 1, "chapter"));

        points.Add(new SuppressionInsertionPoint(SuppressionScope.File, 0));

        return SuppressionCodeActionBuilder.Build(
            documentUri, diagnostic, id, SuppressionCommentFormat.DialogText, lines, points);
    }
}
