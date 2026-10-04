// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     Where the DEBUG file log goes.
///     <para>
///         Serilog resolves a relative path against the CURRENT DIRECTORY, and a server spawned by
///         the extension inherits the editor's. A whole session's log landed in the VS Code
///         installation folder while the workspace the user was looking at showed none at all - and
///         "there are no logs" is exactly the wrong thing to learn when a server will not start. So
///         the path this returns is always absolute, and the caller says where.
///     </para>
/// </summary>
public static class ServerLogPath
{
    /// <summary>The directory the client asks for, normally the workspace root.</summary>
    public const string Option = "--log-dir=";

    /// <summary>
    ///     The project this server serves, for the file name: one server runs per open project, and
    ///     a dependency that is also open writes its log into the same directory as the leaf.
    /// </summary>
    public const string StemOption = "--log-stem=";

    /// <summary>Serilog appends the date; this is the stem it rolls.</summary>
    private const string FileName = "aetswg-.log";

    /// <summary>
    ///     The rolling log file for <paramref name="args" />: under <c>--log-dir=</c> when one is
    ///     passed, under <paramref name="baseDirectory" /> (the server's own directory) otherwise. A
    ///     relative <c>--log-dir</c> resolves against <paramref name="baseDirectory" />, never
    ///     against the current directory - that is the whole point.
    /// </summary>
    public static string Resolve(IReadOnlyList<string> args, string baseDirectory)
    {
        var option = args.LastOrDefault(a => a.StartsWith(Option, StringComparison.Ordinal));
        var asked = option is null ? string.Empty : option[Option.Length..].Trim();

        // Path.Combine keeps an absolute `asked` whole and anchors a relative one.
        var directory = asked.Length == 0 ? baseDirectory : Path.Combine(baseDirectory, asked);
        return Path.Combine(Path.GetFullPath(directory), FileNameFor(args));
    }

    /// <summary><c>aetswg-&lt;stem&gt;-.log</c> when a stem is passed, <c>aetswg-.log</c> otherwise.</summary>
    private static string FileNameFor(IReadOnlyList<string> args)
    {
        var option = args.LastOrDefault(a => a.StartsWith(StemOption, StringComparison.Ordinal));
        var stem = option is null ? string.Empty : option[StemOption.Length..].Trim();
        if (stem.Length == 0) return FileName;

        // A stem is a project name the client took from a path; anything a file name cannot hold
        // becomes an underscore rather than a crash before the first log line.
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(stem.Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray());
        return "aetswg-" + safe + "-.log";
    }
}