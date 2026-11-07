// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

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
}
