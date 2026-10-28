// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Preview;

namespace PG.StarWarsGame.LSP.Preview.Tests;

/// <summary>
///     The one place a particle file's unapplied data gets an id and a severity.
/// </summary>
public sealed class ParticleNoticeProblemsTest
{
    [Fact]
    public void For_GivesUnreadDataAWarningAndIgnoredDataAnInfo_WithTheirIds()
    {
        // Measured on the game's reader (#163): an id it has no case for is skipped - nothing knows
        // what it is, so a warning. Known data it reads past is editor state - information.
        var system = new AlamoParticleContent("p_test", true, [])
        {
            Notices =
            [
                new AlamoParticleNotice(AlamoParticleNoticeKind.UnreadByGame,
                    "Emitter 5 - chunk 0x7F not read by the game, skipped"),
                new AlamoParticleNotice(AlamoParticleNoticeKind.IgnoredByGame,
                    "Emitter 5 - link group 3, Particle Editor data the game does not read")
            ]
        };

        var problems = ParticleNoticeProblems.For(system);

        Assert.Collection(problems,
            p =>
            {
                Assert.Equal(DiagnosticIds.PreviewParticleUnreadByGame, p.DiagnosticId);
                Assert.Equal("warning", p.Severity);
                Assert.Contains("0x7F", p.Message);
            },
            p =>
            {
                Assert.Equal(DiagnosticIds.PreviewParticleIgnoredByGame, p.DiagnosticId);
                Assert.Equal("info", p.Severity);
                Assert.Contains("link group 3", p.Message);
            });
    }

    [Fact]
    public void For_GivesAShippedFileNothing()
    {
        Assert.Empty(ParticleNoticeProblems.For(new AlamoParticleContent("p_smoke", true, [])));
    }
}