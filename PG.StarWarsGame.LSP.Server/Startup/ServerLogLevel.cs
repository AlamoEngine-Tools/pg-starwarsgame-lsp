// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     How loud the server is, from <c>--log-level=</c>.
/// </summary>
/// <remarks>
///     <para>
///         Information by default, which is the right level for a session that is working. It was
///         also the only level, pinned in code, and that cost a diagnosis: the two lines that say
///         whether a document reached the XML sync handler - the one in its
///         <c>CreateRegistrationOptions</c> and the one that is the first statement of its
///         <c>Handle</c> - are both <c>LogDebug</c>, so a session that published nothing produced a
///         log that could not say why. The absence of that second line was read as proof the
///         handler was never entered, when the level suppresses it on every start, good or bad.
///     </para>
///     <para>
///         The last option wins, matching <see cref="ServerLogPath" />, and anything unparseable
///         falls back to the default rather than failing the start - a reader who mistypes a level
///         wants the server they had, not no server and not a silent one.
///     </para>
/// </remarks>
public static class ServerLogLevel
{
    private const string Option = "--log-level=";

    public const LogLevel Default = LogLevel.Information;

    /// <summary>The level <paramref name="args" /> asks for, or <see cref="Default" />.</summary>
    public static LogLevel Resolve(IReadOnlyList<string> args)
    {
        var option = args.LastOrDefault(a => a.StartsWith(Option, StringComparison.Ordinal));
        if (option is null)
            return Default;

        var asked = option[Option.Length..].Trim();
        return Enum.TryParse<LogLevel>(asked, true, out var level) && Enum.IsDefined(level)
            ? level
            : Default;
    }
}
