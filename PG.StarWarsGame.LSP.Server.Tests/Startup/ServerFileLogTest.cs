// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Tests.Startup;

/// <summary>
///     The file log: which level it is written at, if at all, and how long its files stay. Serilog
///     decides a file's age by the date in its name, so the files here are only named, never aged.
/// </summary>
public sealed class ServerFileLogTest : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("aet-filelog-").FullName;

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    private string FileOf(string stem, int daysAgo)
    {
        var date = DateTime.Now.AddDays(-daysAgo).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return Path.Combine(_directory, $"aetswg-{stem}-{date}.log");
    }

    private void WriteOneLine(string stem)
    {
        using var log = ServerFileLog.Create(Path.Combine(_directory, $"aetswg-{stem}-.log"), LogLevel.Debug);
        log.Information("line");
    }

    private string TodaysText(string stem)
    {
        return File.ReadAllText(FileOf(stem, 0));
    }

    [Fact]
    public void Create_WritesTheDaysFile()
    {
        WriteOneLine("Mod");

        Assert.True(File.Exists(FileOf("Mod", 0)));
    }

    [Fact]
    public void Create_RemovesAFileOlderThanTheRetainedDays()
    {
        var old = FileOf("Mod", ServerFileLog.RetainedDays + 2);
        File.WriteAllText(old, "old");

        WriteOneLine("Mod");

        Assert.False(File.Exists(old));
    }

    [Fact]
    public void Create_KeepsAFileInsideTheRetainedDays()
    {
        var recent = FileOf("Mod", ServerFileLog.RetainedDays - 2);
        File.WriteAllText(recent, "recent");

        WriteOneLine("Mod");

        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void Create_LeavesAnotherProjectsFilesToThatProjectsServer()
    {
        var other = FileOf("Other", ServerFileLog.RetainedDays + 2);
        File.WriteAllText(other, "old");

        WriteOneLine("Mod");

        Assert.True(File.Exists(other));
    }

    [Fact]
    public void Create_WritesNothingBelowItsLevel()
    {
        using (var log = ServerFileLog.Create(Path.Combine(_directory, "aetswg-Mod-.log"), LogLevel.Warning))
        {
            log.Information("quiet line");
            log.Warning("loud line");
        }

        var text = TodaysText("Mod");
        Assert.DoesNotContain("quiet line", text);
        Assert.Contains("loud line", text);
    }

    [Theory]
    [InlineData("Trace", LogLevel.Trace)]
    [InlineData("Debug", LogLevel.Debug)]
    [InlineData("information", LogLevel.Information)]
    [InlineData("Warning", LogLevel.Warning)]
    [InlineData("Error", LogLevel.Error)]
    public void Resolve_TakesTheLevelAsked(string asked, LogLevel expected)
    {
        Assert.Equal(expected,
            ServerFileLog.Resolve([$"--log-file-level={asked}"], false));
        Assert.Equal(expected,
            ServerFileLog.Resolve([$"--log-file-level={asked}"], true));
    }

    [Fact]
    public void Resolve_WritesNoFileInAReleaseBuildThatWasNotAsked()
    {
        Assert.Null(ServerFileLog.Resolve([], false));
        Assert.Null(ServerFileLog.Resolve(["--log-level=Debug", "--log-dir=logs"], false));
    }

    [Fact]
    public void Resolve_WritesAtDebugInADebugBuildThatWasNotAsked()
    {
        Assert.Equal(LogLevel.Debug, ServerFileLog.Resolve([], true));
        Assert.Equal(LogLevel.Debug, ServerFileLog.Resolve(["--log-level=Warning"], true));
    }

    [Fact]
    public void Resolve_NoneSwitchesTheFileOffInEveryBuild()
    {
        Assert.Null(ServerFileLog.Resolve(["--log-file-level=None"], true));
        Assert.Null(ServerFileLog.Resolve(["--log-file-level=None"], false));
    }

    [Fact]
    public void Resolve_AnUnreadableLevelIsAsIfNotAsked()
    {
        Assert.Null(ServerFileLog.Resolve(["--log-file-level=loud"], false));
        Assert.Equal(LogLevel.Debug, ServerFileLog.Resolve(["--log-file-level=loud"], true));
        Assert.Null(ServerFileLog.Resolve(["--log-file-level=42"], false));
    }

    [Fact]
    public void Resolve_TheLastOptionWins()
    {
        Assert.Equal(LogLevel.Warning,
            ServerFileLog.Resolve(["--log-file-level=Debug", "--log-file-level=Warning"], false));
    }

    /// <summary>
    ///     The file's level is its own: a tester's file is asked for at Debug while the server's
    ///     level, which the output channel shows, stays where it was.
    /// </summary>
    [Fact]
    public void Apply_WritesTheFileAtItsOwnLevelWhateverTheServersLevel()
    {
        var log = ServerFileLog.Create(Path.Combine(_directory, "aetswg-Mod-.log"), LogLevel.Debug);
        using (var factory = LoggerFactory.Create(builder =>
                   ServerFileLog.Apply(ServerLogLevel.Apply(builder, LogLevel.Warning), LogLevel.Debug, log)))
        {
            var logger = factory.CreateLogger("Probe");
            logger.LogTrace("trace line");
            logger.LogDebug("debug line");
            logger.LogInformation("information line");
        }

        var text = TodaysText("Mod");
        Assert.DoesNotContain("trace line", text);
        Assert.Contains("debug line", text);
        Assert.Contains("information line", text);
    }

    [Fact]
    public void Apply_AddsNothingWhenNoFileIsWritten()
    {
        using var factory = LoggerFactory.Create(builder =>
            ServerFileLog.Apply(ServerLogLevel.Apply(builder, LogLevel.Information), null));

        factory.CreateLogger("Probe").LogInformation("line");

        Assert.Empty(Directory.GetFiles(_directory));
    }
}