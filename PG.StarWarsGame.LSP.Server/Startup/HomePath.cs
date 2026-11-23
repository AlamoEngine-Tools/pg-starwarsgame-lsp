// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.RegularExpressions;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     The home directory in a line the server writes before logging is up, written as <c>~</c>.
///     <para>
///         Those lines go to the error stream, which the client shows in the project's output
///         channel, and they name the log folder - a path under the home directory, which carries
///         the user's name. The channel is read on screen and pasted into public issues.
///     </para>
/// </summary>
public static class HomePath
{
    /// <summary><paramref name="text" /> with every path under this user's home directory starting at <c>~</c>.</summary>
    public static string AsTilde(string text)
    {
        return AsTilde(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            OperatingSystem.IsWindows());
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
        var parts = home.TrimEnd('\\', '/').Split('\\', '/');
        if (parts.All(part => part.Length == 0))
            return text;

        var path = string.Join(@"[\\/]", parts.Select(Regex.Escape)) + @"(?=[\\/]|[^\w.-]|$)";
        var options = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        return Regex.Replace(text, path, "~", options);
    }
}