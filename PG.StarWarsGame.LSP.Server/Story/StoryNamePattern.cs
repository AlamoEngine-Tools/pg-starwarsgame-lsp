// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     The story graph's event-name filter, read the way the search plate reads every pattern: as
///     plain text, with wildcards, or as a regular expression - all without case, all found anywhere
///     in the name. The webview's own lists match the same three ways; this is the half the server
///     answers, because the graph's filter is applied here.
/// </summary>
public static class StoryNamePattern
{
    /// <summary>The mode a pattern is read in when none is given.</summary>
    public const string TextMode = "text";

    /// <summary><c>*</c> is any text and <c>?</c> one character; everything else means itself.</summary>
    public const string WildcardMode = "wildcard";

    /// <summary>A regular expression in the syntax the webview's search compiles.</summary>
    public const string RegexMode = "regex";

    /// <summary>
    ///     How long one name may take to answer a regular expression. A pattern is typed by a
    ///     reader and run against every event of a campaign; one that backtracks without end answers
    ///     "no" for that name rather than holding the request.
    /// </summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Compiles a pattern into a test of one name.</summary>
    /// <param name="pattern">The pattern as typed. Null or empty answers every name.</param>
    /// <param name="mode"><see cref="TextMode" />, <see cref="WildcardMode" /> or <see cref="RegexMode" />; null is text.</param>
    /// <exception cref="ArgumentException">The mode is none of the three.</exception>
    public static Func<string, bool> Compile(string? pattern, string? mode)
    {
        var read = mode ?? TextMode;
        if (read is not (TextMode or WildcardMode or RegexMode))
            throw new ArgumentException(
                // ReSharper disable once LocalizableElement - a protocol fault for the maintainers, not a text a reader sees.
                $"Unknown name filter mode '{read}'; expected '{TextMode}', '{WildcardMode}' or '{RegexMode}'.",
                nameof(mode));

        if (string.IsNullOrEmpty(pattern))
            return _ => true;

        if (read == TextMode)
            return name => name.Contains(pattern, StringComparison.OrdinalIgnoreCase);

        // Escaped first, so only * and ? are special - a dot in a name would otherwise match any
        // character and quietly widen the filter. Nothing here can fail to compile.
        var expression = read == WildcardMode
            ? Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".")
            : pattern;

        Regex compiled;
        try
        {
            // ECMAScript, because the webview compiles the same pattern as JavaScript to say whether
            // it is valid: the two should agree on what a pattern means.
            compiled = new Regex(
                expression, RegexOptions.ECMAScript | RegexOptions.IgnoreCase, MatchTimeout);
        }
        catch (ArgumentException)
        {
            // Half-typed most of the time it is used. It matches nothing; the webview says why.
            return _ => false;
        }

        return name =>
        {
            try
            {
                return compiled.IsMatch(name);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        };
    }
}
