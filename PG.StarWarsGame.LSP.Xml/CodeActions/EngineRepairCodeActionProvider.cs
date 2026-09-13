// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Xml.CodeActions;

/// <summary>
///     Offers the correction the ENGINE applies, where it lands somewhere other than the reported
///     range.
/// </summary>
/// <remarks>
///     <para>
///         Separate from <see cref="FixSuggestionCodeActionProvider" />, which can only replace the
///         diagnostic's own range. These repairs edit a different tag, or two of them at once - the
///         automatic-style rule reports an ability and turns off a child flag, the respawn rule
///         exchanges a pair of values.
///     </para>
///     <para>
///         The edits arrive precomputed in the diagnostic's data, in document coordinates, because
///         the rule that built the diagnostic already held the parsed nodes. Nothing here re-reads
///         or re-parses the document.
///     </para>
/// </remarks>
internal sealed class EngineRepairCodeActionProvider : IXmlCodeActionProvider
{
    public IEnumerable<CommandOrCodeAction> Handle(XmlCodeActionContext ctx)
    {
        var repair = ctx.Diagnostic.Data?["engineRepair"];
        if (repair is null) return [];

        var title = (string?)repair["title"];
        var edits = repair["edits"]?
            .Select(e => new TextEdit
            {
                NewText = (string?)e["newText"] ?? string.Empty,
                Range = RangeOf((int?)e["line"] ?? 0, (int?)e["column"] ?? 0, (int?)e["length"] ?? 0),
            })
            .ToList();

        if (title is null || edits is null || edits.Count == 0) return [];

        return
        [
            new CommandOrCodeAction(new CodeAction
            {
                Title = title,
                Kind = CodeActionKind.QuickFix,
                Diagnostics = new Container<Diagnostic>(ctx.Diagnostic),
                // Not preferred: applying it changes nothing about how the game behaves, so it
                // should never be what a "fix all" reaches for ahead of a real correction.
                IsPreferred = false,
                Edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                    {
                        [ctx.DocumentUri] = edits,
                    },
                },
            })
        ];
    }

    private static LspRange RangeOf(int line, int column, int length)
    {
        return new LspRange(new Position(line, column), new Position(line, column + length));
    }
}
