// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Project;

namespace PG.StarWarsGame.LSP.Core.Tests.Configuration;

public sealed class BaselineSourceConfigTest
{
    // The baseline repository lays the two games out side by side, one folder each.
    [Theory]
    [InlineData(TargetGame.EAW, "eaw/aet-pg-swg-lsp-eaw-baseline.aet")]
    [InlineData(TargetGame.FOC, "foc/aet-pg-swg-lsp-foc-baseline.aet")]
    public void ForGame_NamesThatGamesFile(TargetGame game, string expected)
    {
        Assert.Equal(expected, new BaselineSourceConfig().ForGame(game).FilePath);
    }

    // A URL or a local path the user named is theirs: the game only picks the file where the
    // source is a repository layout.
    [Fact]
    public void ForGame_KeepsAnExplicitUrlAndLocalPath()
    {
        var source = new BaselineSourceConfig
        {
            Type = BaselineSourceType.Local, Url = "https://example.test/x.aet", LocalPath = "/baselines"
        };

        var forEaw = source.ForGame(TargetGame.EAW);

        Assert.Equal("https://example.test/x.aet", forEaw.Url);
        Assert.Equal("/baselines", forEaw.LocalPath);
        Assert.Equal(BaselineSourceType.Local, forEaw.Type);
    }

    // ── the local file each game's server reads (#288) ─────────────────────────

    private const string Foc = @"C:\baselines\foc\aet-pg-swg-lsp-foc-baseline.aet";
    private const string Eaw = @"C:\baselines\eaw\aet-pg-swg-lsp-eaw-baseline.aet";

    private static BaselineSourceConfig Local(string path)
    {
        return new BaselineSourceConfig { Type = BaselineSourceType.Local, LocalPath = path };
    }

    [Fact]
    public void LocalFile_AFolder_IsThisGamesFileInTheRepositoryLayout()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData> { [Foc] = new(""), [Eaw] = new("") });

        Assert.Equal(Eaw, Local(@"C:\baselines").ForGame(TargetGame.EAW).LocalFile(fileSystem));
        Assert.Equal(Foc, Local(@"C:\baselines").ForGame(TargetGame.FOC).LocalFile(fileSystem));
    }

    /// <summary>
    ///     One setting serves the whole window, so in a window with an Empire at War project and a
    ///     Forces of Corruption project it names one file - the tester's named the FoC file, and the
    ///     EaW project's server loaded it (measured 2026-10-22). The file for the other game in the
    ///     same layout is this game's file, when it is there.
    /// </summary>
    [Fact]
    public void LocalFile_TheOtherGamesFileInTheRepositoryLayout_IsThisGamesSibling()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData> { [Foc] = new(""), [Eaw] = new("") });

        Assert.Equal(Eaw, Local(Foc).ForGame(TargetGame.EAW).LocalFile(fileSystem));
        Assert.Equal(Foc, Local(Foc).ForGame(TargetGame.FOC).LocalFile(fileSystem));
        Assert.Equal(Foc, Local(Eaw).ForGame(TargetGame.FOC).LocalFile(fileSystem));
    }

    // A file named outright with no sibling for this game, or a file of the user's own naming, is read as
    // named: the user chose it, and a guess would load nothing.
    [Fact]
    public void LocalFile_AFileWithNoSiblingForThisGame_StaysAsNamed()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Foc] = new(""), [@"C:\mine\my-baseline.aet"] = new("")
        });

        Assert.Equal(Foc, Local(Foc).ForGame(TargetGame.EAW).LocalFile(fileSystem));
        Assert.Equal(@"C:\mine\my-baseline.aet",
            Local(@"C:\mine\my-baseline.aet").ForGame(TargetGame.EAW).LocalFile(fileSystem));
    }
}