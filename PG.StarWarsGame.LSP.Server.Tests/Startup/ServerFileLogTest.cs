// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Tests.Startup;

/// <summary>
///     The file log keeps a fixed number of days. Serilog decides a file's age by the date in its
///     name, so the files here are only named, never aged.
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
        using var log = ServerFileLog.Create(Path.Combine(_directory, $"aetswg-{stem}-.log"));
        log.Information("line");
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
}