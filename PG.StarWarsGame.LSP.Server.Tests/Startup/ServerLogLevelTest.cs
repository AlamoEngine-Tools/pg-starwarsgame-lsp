// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Tests.Startup;

/// <summary>
///     How loud the server is, decided by the command line.
/// </summary>
/// <remarks>
///     The level was pinned to Information in code. That is the right default and the wrong ONLY
///     option: the two lines that say whether a document reached the XML sync handler are both
///     <c>LogDebug</c>, so a session that publishes no diagnostics produced a log that could not
///     answer why - and an outage was diagnosed as "the handler is never entered" from the absence
///     of a line the level always suppressed. Raising it must not need a rebuild.
/// </remarks>
public sealed class ServerLogLevelTest
{
    [Fact]
    public void WithNoOption_IsInformation()
    {
        Assert.Equal(LogLevel.Information, ServerLogLevel.Resolve([]));
        Assert.Equal(LogLevel.Information, ServerLogLevel.Resolve(["--tcp=9000"]));
    }

    [Theory]
    [InlineData("--log-level=Debug", LogLevel.Debug)]
    [InlineData("--log-level=debug", LogLevel.Debug)]
    [InlineData("--log-level=TRACE", LogLevel.Trace)]
    [InlineData("--log-level=Warning", LogLevel.Warning)]
    public void ItReadsTheOption(string arg, LogLevel expected)
    {
        Assert.Equal(expected, ServerLogLevel.Resolve([arg]));
    }

    /// <summary>
    ///     A level nobody can parse must not take the server down, and must not go quiet either -
    ///     the default is what the reader expected before they passed the option.
    /// </summary>
    [Fact]
    public void AnUnparseableLevel_FallsBackToInformation()
    {
        Assert.Equal(LogLevel.Information, ServerLogLevel.Resolve(["--log-level=louder"]));
        Assert.Equal(LogLevel.Information, ServerLogLevel.Resolve(["--log-level="]));
    }

    /// <summary>The last one wins, matching how the log PATH option already behaves.</summary>
    [Fact]
    public void TheLastOptionWins()
    {
        Assert.Equal(LogLevel.Warning,
            ServerLogLevel.Resolve(["--log-level=Debug", "--log-level=Warning"]));
    }

    /// <summary>
    ///     Every level the <c>aet-eaw-edit.lsp.debug.logLevel</c> setting documents is one this server
    ///     understands.
    /// </summary>
    /// <remarks>
    ///     The setting hands its value straight to <c>--log-level=</c>, and an unparseable one falls
    ///     back to Information SILENTLY - so a level the extension README names and this server does
    ///     not parse would quietly do nothing. The setting is no longer contributed, so the README's
    ///     list is the one checked here.
    /// </remarks>
    [Theory]
    [InlineData("Trace")]
    [InlineData("Debug")]
    [InlineData("Information")]
    [InlineData("Warning")]
    [InlineData("Error")]
    public void EveryLevelTheSettingDocumentsIsUnderstood(string level)
    {
        Assert.Equal(Enum.Parse<LogLevel>(level), ServerLogLevel.Resolve([$"--log-level={level}"]));
    }

    /// <summary>
    ///     The client passes the option only when the setting DIFFERS from Information, so the
    ///     server's own default has to be that level.
    /// </summary>
    [Fact]
    public void TheDefault_IsTheLevelTheClientLeavesUnsaid()
    {
        Assert.Equal(LogLevel.Information, ServerLogLevel.Default);
    }
}