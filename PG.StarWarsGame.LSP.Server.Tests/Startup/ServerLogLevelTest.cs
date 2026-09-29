// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
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
    ///     Every level the VS Code setting offers is one this server understands.
    /// </summary>
    /// <remarks>
    ///     The setting hands its value straight to <c>--log-level=</c>, and an unparseable one falls
    ///     back to Information SILENTLY - so a typo in the manifest's enum would present a level
    ///     that quietly does nothing. The two lists have to be checked against each other.
    /// </remarks>
    [Fact]
    public void EveryLevelTheVsCodeSettingOffersIsUnderstood()
    {
        var manifest = Path.Combine(FindRepoRoot(),
            "PG.StarWarsGame.LSP.Client.VSCode", "aet-eaw-edit", "package.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));

        var setting = doc.RootElement
            .GetProperty("contributes").GetProperty("configuration").EnumerateArray()
            .Select(block => block.TryGetProperty("properties", out var props)
                             && props.TryGetProperty("aet-eaw-edit.lsp.debug.logLevel", out var s)
                ? s
                : (JsonElement?)null)
            .FirstOrDefault(s => s is not null);
        Assert.NotNull(setting);

        var offered = setting!.Value.GetProperty("enum").EnumerateArray()
            .Select(v => v.GetString()!).ToArray();
        Assert.NotEmpty(offered);

        foreach (var level in offered)
            Assert.True(
                ServerLogLevel.Resolve([$"--log-level={level}"]) != ServerLogLevel.Default
                || level == nameof(LogLevel.Information),
                $"The setting offers '{level}', which this server does not understand - it would " +
                "silently fall back to Information.");

        // And the declared default is the one the server would have used anyway - the client only
        // passes the option when it DIFFERS from this, so the two have to name the same level.
        Assert.Equal<string?>(ServerLogLevel.Default.ToString(),
            setting.Value.GetProperty("default").GetString());
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PG.StarWarsGame.LSP.slnx")))
            dir = dir.Parent;
        return dir?.FullName
               ?? throw new InvalidOperationException("Could not locate the repo root.");
    }
}