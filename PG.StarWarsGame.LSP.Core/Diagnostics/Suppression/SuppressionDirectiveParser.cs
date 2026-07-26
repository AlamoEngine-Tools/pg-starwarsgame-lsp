// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;

namespace PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;

/// <summary>
///     One suppression directive as written, before it is resolved against a document.
/// </summary>
/// <param name="Matchers">
///     Everything the directive names. More than one because a single comment can silence a list -
///     the shadow diagnostics, for instance, are two ids that a user almost always wants together.
/// </param>
/// <param name="Scope">Scope keyword used, which decides what lines the directive will cover.</param>
/// <param name="Reason">
///     Free text after <c>reason::</c>, or null. Carried for reporting - notably to explain an
///     unused suppression back to whoever wrote it - never for matching.
/// </param>
public sealed record SuppressionDirective(
    IReadOnlyList<SuppressionMatcher> Matchers,
    SuppressionScope Scope,
    string? Reason);

/// <summary>
///     What a comment turned out to be.
/// </summary>
/// <param name="IsDirective">
///     Whether the comment was a directive at all. Distinguishes "ordinary prose", which is not the
///     suppression system's business, from "a directive that named nothing", which is.
/// </param>
/// <param name="Directive">The directive, or null when not one entry of its list parsed.</param>
/// <param name="Problems">Everything wrong with it, empty when the comment is not a directive.</param>
public sealed record SuppressionDirectiveResult(
    bool IsDirective,
    SuppressionDirective? Directive,
    IReadOnlyList<SuppressionDirectiveProblem> Problems)
{
    public static readonly SuppressionDirectiveResult NotADirective = new(false, null, []);
}

/// <summary>
///     The directive grammar, independent of any document model:
///     <code>
///     suppression ::= "aetswg:" keyword ws rule-list (ws "reason::" ws? text)?
///     keyword     ::= "suppress" | "suppress-object" | "suppress-file"
///     rule-list   ::= rule-id ("," ws? rule-id)*
///     rule-id     ::= "aetswg-" group "-" (number | "*")
///     </code>
///     <para>
///         Callers pass a comment body with its language's delimiters already stripped, which is
///         all that differs between XML, Lua and dialog text. Resolving a scope to a line span
///         needs the document tree and so stays with the caller; <see cref="SuppressionSpans" />
///         holds the part of that which is language-independent.
///     </para>
/// </summary>
public static partial class SuppressionDirectiveParser
{
    private const string ReasonMarker = "reason::";

    [GeneratedRegex(@"aetswg:suppress(?<scope>-object|-file)?(?![-\w])(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex DirectiveRegex();

    /// <summary>
    ///     Parses a directive out of a comment body. Fails when there is no directive, and also
    ///     when there is one but not a single id in it parses - a directive that names nothing
    ///     valid must silence nothing, so its diagnostics stay visible rather than being hidden by
    ///     a typo.
    ///     <para>
    ///         Use <see cref="Parse" /> where the caller can report: this overload drops the reason
    ///         a directive was rejected, which is the whole of what the user needs to fix it.
    ///     </para>
    /// </summary>
    public static bool TryParse(string? commentBody, out SuppressionDirective directive)
    {
        var result = Parse(commentBody);
        directive = result.Directive!;
        return result.Directive is not null;
    }

    /// <summary>
    ///     Parses a comment body, reporting what was wrong with it as well as what it means.
    /// </summary>
    public static SuppressionDirectiveResult Parse(string? commentBody)
    {
        if (string.IsNullOrWhiteSpace(commentBody)) return SuppressionDirectiveResult.NotADirective;

        var match = DirectiveRegex().Match(commentBody);
        if (!match.Success) return SuppressionDirectiveResult.NotADirective;

        var scope = match.Groups["scope"].Value.ToLowerInvariant() switch
        {
            "-file" => SuppressionScope.File,
            "-object" => SuppressionScope.Object,
            _ => SuppressionScope.Node
        };

        var (rules, reason) = SplitOffReason(match.Groups["rest"].Value);

        // Each entry is parsed on its own: one malformed id costs the user only that entry, not the
        // ones they got right, and is reported by itself. Only the leading token of an entry is the
        // id, so prose trailing the list is ignored rather than invalidating the entry it follows.
        var matchers = new List<SuppressionMatcher>();
        var problems = new List<SuppressionDirectiveProblem>();

        foreach (var entry in rules.Split(','))
        {
            var token = FirstToken(entry);

            // An empty entry is a stray or trailing comma. It claims nothing about a diagnostic,
            // so there is nothing to tell the user beyond noise.
            if (token.Length == 0) continue;

            if (SuppressionMatcher.TryParse(token, out var matcher))
                matchers.Add(matcher);
            else
                problems.Add(new SuppressionDirectiveProblem(SuppressionProblemKind.UnknownRule, token));
        }

        // Only when nothing was written at all: a list of unreadable entries has already been
        // reported entry by entry, and saying "you named nothing" on top of that adds no
        // information the user does not already have.
        if (matchers.Count == 0 && problems.Count == 0)
            problems.Add(new SuppressionDirectiveProblem(SuppressionProblemKind.NoRules, string.Empty));

        var directive = matchers.Count == 0
            ? null
            : new SuppressionDirective(matchers, scope, reason);

        return new SuppressionDirectiveResult(true, directive, problems);
    }

    private static string FirstToken(string entry)
    {
        var trimmed = entry.AsSpan().TrimStart();
        var end = trimmed.IndexOfAny(" \t\r\n");
        return (end < 0 ? trimmed.TrimEnd() : trimmed[..end]).ToString();
    }

    /// <summary>
    ///     Splits the rule list from the reason. Everything after the marker is free text, so the
    ///     split happens before any comma handling - otherwise a comma in the prose would be read
    ///     as another rule id.
    /// </summary>
    private static (string Rules, string? Reason) SplitOffReason(string rest)
    {
        var marker = rest.IndexOf(ReasonMarker, StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return (rest, null);

        var reason = rest[(marker + ReasonMarker.Length)..].Trim();
        return (rest[..marker], reason.Length == 0 ? null : reason);
    }
}

/// <summary>
///     The scope-to-span rules that do not depend on a document model, shared by every language.
/// </summary>
public static class SuppressionSpans
{
    /// <summary>A file-scoped directive covers everything, wherever in the file it sits.</summary>
    public static (int Start, int End) File => (0, int.MaxValue);

    /// <summary>
    ///     Span of a directive anchored to some target element. A directive whose target does not
    ///     exist - an object-scoped comment outside any object, or a trailing node-scoped comment
    ///     with nothing after it - collapses onto its own line rather than covering the rest of the
    ///     document. Silencing more than was asked for is the worse failure.
    /// </summary>
    public static (int Start, int End) ForTarget((int Start, int End)? target, int directiveLine)
    {
        if (target is not { } t) return (directiveLine, directiveLine);
        return (Math.Min(t.Start, directiveLine), Math.Max(t.End, t.Start));
    }
}
