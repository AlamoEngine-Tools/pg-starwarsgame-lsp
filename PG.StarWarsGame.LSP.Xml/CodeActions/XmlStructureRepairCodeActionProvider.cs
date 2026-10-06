// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Util;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.CodeActions;

/// <summary>
///     Repairs for structural findings. The current text is read again and the finding at the
///     diagnostic's own position supplies its repair, so a stale diagnostic offers nothing. Beside
///     the single repair, "fix all" repairs every repairable finding in the file the project has not
///     suppressed, re-reading between passes because the game's reader stops at its first error.
/// </summary>
internal sealed class XmlStructureRepairCodeActionProvider : IXmlCodeActionProvider
{
    public const string FixAllTitle = "Fix all repairable XML structure problems in this file";

    private readonly IFileHelper _fileHelper;
    private readonly IXmlParseCache _parseCache;
    private readonly IGlobalSuppressionStore _suppressions;
    private readonly IXmlStructuralValidator _validator;

    public XmlStructureRepairCodeActionProvider(IXmlParseCache parseCache, IFileHelper fileHelper,
        IXmlStructuralValidator validator, IGlobalSuppressionStore suppressions)
    {
        _parseCache = parseCache;
        _fileHelper = fileHelper;
        _validator = validator;
        _suppressions = suppressions;
    }

    public IEnumerable<CommandOrCodeAction> Handle(XmlCodeActionContext ctx)
    {
        if (!XmlStructureHandler.TryGetCategory(ctx.Diagnostic.Code?.String, out var category))
            return [];

        var parsed = _parseCache.GetOrParse(_fileHelper.NormalizeUri(ctx.DocumentUri.ToString()));
        if (parsed is null) return [];
        var text = parsed.Text;
        var findings = _validator.Validate(text);

        var start = ctx.Diagnostic.Range.Start;
        var finding = findings.FirstOrDefault(f =>
            f.Category == category && f.Line == start.Line && f.Column == start.Character);
        if (finding is null) return [];

        var lines = new LineOffsetIndex(text);
        var actions = new List<CommandOrCodeAction>();
        if (finding.Repair is { } repair)
            actions.Add(new CommandOrCodeAction(new CodeAction
            {
                Title = repair.Title,
                Kind = CodeActionKind.QuickFix,
                IsPreferred = true,
                Diagnostics = new Container<Diagnostic>(ctx.Diagnostic),
                Edit = Edit(ctx.DocumentUri, repair.Edits.Select(e => new TextEdit
                {
                    Range = new LspRange(Position(lines, e.Start), Position(lines, e.Start + e.Length)),
                    NewText = e.NewText
                }))
            }));

        var suppressed = _suppressions.GetAll();
        var (fixedText, _) = XmlStructureRepairs.FixAll(text, _validator,
            c => !suppressed.Any(m => m.Matches(XmlStructureHandler.IdOf(c))));
        if (!string.Equals(fixedText, text, StringComparison.Ordinal))
            actions.Add(new CommandOrCodeAction(new CodeAction
            {
                Title = FixAllTitle,
                Kind = CodeActionKind.QuickFix,
                Diagnostics = new Container<Diagnostic>(ctx.Diagnostic),
                Edit = Edit(ctx.DocumentUri,
                [
                    new TextEdit
                    {
                        Range = new LspRange(new Position(0, 0), Position(lines, text.Length)),
                        NewText = fixedText
                    }
                ])
            }));

        return actions;
    }

    private static Position Position(LineOffsetIndex lines, int offset)
    {
        var (line, col) = lines.GetPosition(offset);
        return new Position(line, col);
    }

    private static WorkspaceEdit Edit(DocumentUri uri, IEnumerable<TextEdit> edits)
    {
        return new WorkspaceEdit
            { Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>> { [uri] = edits.ToList() } };
    }
}