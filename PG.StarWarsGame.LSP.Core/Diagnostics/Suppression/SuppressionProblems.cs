// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;

/// <summary>What is wrong with a suppression directive.</summary>
public enum SuppressionProblemKind
{
    /// <summary>An entry in the rule list that is neither an id nor a group wildcard.</summary>
    UnknownRule,

    /// <summary>A directive that names no rules at all.</summary>
    NoRules
}

/// <summary>
///     Something wrong with a directive, found while parsing the grammar and so not yet tied to a
///     line. Call <see cref="At" /> once the caller knows where the comment sits.
///     <para>
///         These exist because the parser deliberately refuses to guess: an entry it cannot read
///         suppresses nothing, which without a report is indistinguishable from a diagnostic that
///         will not go away. Silently ignoring a typo is the one failure the user cannot debug.
///     </para>
/// </summary>
public sealed record SuppressionDirectiveProblem(SuppressionProblemKind Kind, string Text)
{
    public DiagnosticId Id => Kind == SuppressionProblemKind.NoRules
        ? DiagnosticIds.SuppressionNoRules
        : DiagnosticIds.SuppressionUnknownRule;

    /// <summary>
    ///     Wording lives here rather than in each language so the same mistake reads the same way
    ///     in XML, Lua and dialog text.
    /// </summary>
    public string Message => Kind == SuppressionProblemKind.NoRules
        ? "Suppression directive names no diagnostic and so suppresses nothing. "
          + "Add an id, for example: aetswg:suppress aetswg-004-0001"
        : $"'{Text}' is not a diagnostic id, so this entry suppresses nothing. "
          + "Expected aetswg-<group>-<number> or aetswg-<group>-*, for example aetswg-004-0001.";

    /// <summary>Anchors the problem to the line its directive was written on.</summary>
    public SuppressionCommentProblem At(int line)
    {
        return new SuppressionCommentProblem(line, Id, Message);
    }
}

/// <summary>A directive problem placed on a line of a specific document.</summary>
public sealed record SuppressionCommentProblem(int Line, DiagnosticId Id, string Message)
{
    /// <summary>
    ///     The publishable diagnostic. A warning, not an error: the document is fine, it is the
    ///     comment about the document that is not.
    /// </summary>
    public Diagnostic ToDiagnostic(string[] lines)
    {
        var length = Line >= 0 && Line < lines.Length ? lines[Line].TrimEnd('\r').Length : 0;

        return new Diagnostic
        {
            Severity = DiagnosticSeverity.Warning,
            Message = Message,
            Code = new DiagnosticCode(Id.ToString()),
            Source = AppProperties.LspServerId,
            Range = new LspRange(new Position(Line, 0), new Position(Line, length))
        };
    }
}

/// <summary>
///     What one document's suppression comments amounted to: the ranges now in force, and anything
///     wrong with the directives that produced them.
///     <para>
///         Both come back from a single walk, and together, so that a caller cannot take the
///         suppressions while quietly dropping the reasons they might not work.
///     </para>
/// </summary>
public sealed record SuppressionScan(
    IReadOnlyList<SuppressionRange> Ranges,
    IReadOnlyList<SuppressionCommentProblem> Problems)
{
    public static readonly SuppressionScan Empty = new([], []);

    /// <summary>The problems as publishable diagnostics, ready to join the document's own.</summary>
    public IEnumerable<Diagnostic> ProblemDiagnostics(string[] lines)
    {
        return Problems.Select(p => p.ToDiagnostic(lines));
    }
}
