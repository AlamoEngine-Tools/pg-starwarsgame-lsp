// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     The home directory in a line the server writes itself, written as <c>~</c>.
///     <para>
///         A path under the home directory carries the user's name, and what the server writes is
///         read by others: its log file is attached to public issues, and the lines it writes to
///         the error stream before logging is up land in the project's output channel. The client
///         does the same for everything else that channel shows.
///     </para>
/// </summary>
public static class HomePath
{
    private static readonly Lazy<Regex?> OwnHome = new(() =>
        PatternOf(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), OperatingSystem.IsWindows()));

    /// <summary><paramref name="text" /> with every path under this user's home directory starting at <c>~</c>.</summary>
    public static string AsTilde(string text)
    {
        return OwnHome.Value?.Replace(text, "~") ?? text;
    }

    /// <summary>
    ///     <paramref name="text" /> with every path under <paramref name="home" /> starting at
    ///     <c>~</c>. A path is taken with either separator; a folder that only starts like the home
    ///     directory is another folder and stays; an empty home directory changes nothing.
    /// </summary>
    /// <param name="text">The line.</param>
    /// <param name="home">The home directory.</param>
    /// <param name="ignoreCase">Whether the file system matches names without regard to case.</param>
    public static string AsTilde(string text, string home, bool ignoreCase)
    {
        return PatternOf(home, ignoreCase)?.Replace(text, "~") ?? text;
    }

    private static Regex? PatternOf(string home, bool ignoreCase)
    {
        var parts = home.TrimEnd('\\', '/').Split('\\', '/');
        if (parts.All(part => part.Length == 0))
            return null;

        var path = string.Join(@"[\\/]", parts.Select(Regex.Escape)) + @"(?=[\\/]|[^\w.-]|$)";
        return new Regex(path,
            RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None));
    }
}