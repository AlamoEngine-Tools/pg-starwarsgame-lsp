// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;

/// <summary>
///     How a language writes a comment, which is the only part of a suppression quick fix that
///     differs between them.
/// </summary>
public sealed record SuppressionCommentFormat(string Prefix, string Suffix)
{
    public static readonly SuppressionCommentFormat Xml = new("<!-- ", " -->");
    public static readonly SuppressionCommentFormat Lua = new("-- ", "");
    public static readonly SuppressionCommentFormat DialogText = new("# ", "");

    public string Wrap(string directive)
    {
        return $"{Prefix}{directive}{Suffix}";
    }
}

/// <summary>
///     One offered scope: where its directive would go, and what to call the thing it covers.
/// </summary>
/// <param name="Scope">Scope to write. <see cref="SuppressionScope.Global" /> is not valid here.</param>
/// <param name="Line">0-based line the comment is inserted above.</param>
/// <param name="TargetLabel">
///     What the scope covers, for the action title - <c>&lt;Unit&gt;</c>, <c>function</c>,
///     <c>chapter</c>. Only used by <see cref="SuppressionScope.Object" />.
/// </param>
public sealed record SuppressionInsertionPoint(SuppressionScope Scope, int Line, string? TargetLabel = null);

/// <summary>
///     Builds the suppression quick fixes. Every language offers the same actions in the same
///     order, with the same titles, differing only in comment syntax and in what its scopes anchor
///     to - so the shape lives here and each language supplies only its own part.
///     <para>
///         None of these are <c>IsPreferred</c>: silencing a diagnostic is never the fix a user
///         should land on by reflex, and marking one preferred would put it behind a single
///         keystroke ahead of the actual repair.
///     </para>
/// </summary>
public static class SuppressionCodeActionBuilder
{
    /// <summary>
    ///     Actions for one diagnostic, narrowest scope first - whichever the user picks by reflex
    ///     should be the least destructive. Callers pass only the scopes that apply where the
    ///     diagnostic sits; a document-scoped action is never offered without a target.
    /// </summary>
    public static IReadOnlyList<CommandOrCodeAction> Build(
        DocumentUri documentUri,
        Diagnostic diagnostic,
        DiagnosticId id,
        SuppressionCommentFormat format,
        string[] lines,
        IReadOnlyList<SuppressionInsertionPoint> points)
    {
        var actions = points
            .Where(p => p.Scope != SuppressionScope.Global)
            .Select(p => Insert(documentUri, diagnostic, id, format, lines, p))
            .ToList();

        // Project-wide suppression writes .aetswg/suppressions.json rather than the open document,
        // so it goes back to the server instead of being a text edit the client applies.
        actions.Add(new CommandOrCodeAction(new CodeAction
        {
            Title = $"Suppress {id} across the project",
            Kind = CodeActionKind.QuickFix,
            Diagnostics = new Container<Diagnostic>(diagnostic),
            Command = new Command
            {
                Name = SuppressionCommands.SuppressGlobally,
                Title = "Suppress diagnostic across the project",
                Arguments = new JArray(id.ToString())
            }
        }));

        return actions;
    }

    private static CommandOrCodeAction Insert(
        DocumentUri documentUri,
        Diagnostic diagnostic,
        DiagnosticId id,
        SuppressionCommentFormat format,
        string[] lines,
        SuppressionInsertionPoint point)
    {
        var directive = $"aetswg:suppress{ScopeSuffix(point.Scope)} {id}";
        var text = $"{IndentOf(lines, point.Line)}{format.Wrap(directive)}\n";

        return new CommandOrCodeAction(new CodeAction
        {
            Title = TitleFor(point, id),
            Kind = CodeActionKind.QuickFix,
            Diagnostics = new Container<Diagnostic>(diagnostic),
            Edit = new WorkspaceEdit
            {
                Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                {
                    [documentUri] =
                    [
                        new TextEdit
                        {
                            Range = new LspRange(
                                new Position(point.Line, 0), new Position(point.Line, 0)),
                            NewText = text
                        }
                    ]
                }
            }
        });
    }

    private static string ScopeSuffix(SuppressionScope scope)
    {
        return scope switch
        {
            SuppressionScope.File => "-file",
            SuppressionScope.Object => "-object",
            _ => string.Empty
        };
    }

    private static string TitleFor(SuppressionInsertionPoint point, DiagnosticId id)
    {
        return point.Scope switch
        {
            SuppressionScope.File => $"Suppress {id} in this file",
            SuppressionScope.Object => $"Suppress {id} for this {point.TargetLabel}",
            _ => $"Suppress {id} for this line"
        };
    }

    /// <summary>Indent of the line being guarded, so the result needs no reformatting.</summary>
    private static string IndentOf(string[] lines, int line)
    {
        if (line < 0 || line >= lines.Length) return string.Empty;

        var source = lines[line];
        var length = 0;
        while (length < source.Length && char.IsWhiteSpace(source[length])) length++;
        return source[..length];
    }
}
