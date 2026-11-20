// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Preview;

namespace PG.StarWarsGame.LSP.Preview.Tests;

/// <summary>
///     The one place a particle file's unapplied data gets an id and a severity.
/// </summary>
public sealed class ParticleNoticeProblemsTest
{
    private static readonly IMessageCatalog Messages = ResxMessageCatalog.Server("en");

    [Fact]
    public void For_SaysEachSkippedChunkOncePerFile_UnreadAsAWarningAndTheLinkGroupAsAnInfo()
    {
        // Measured on the game's reader (#163): a chunk id it has no case for is opened, left unread
        // and closed - nothing knows what it is, so a warning. The Particle Editor's link group is
        // such a chunk too, but known editor data - information. Once per file, naming the emitters
        // (#394): "skipped" on every emitter's line read as if the whole system were.
        var system = new AlamoParticleContent("p_test", true, [])
        {
            SkippedChunks =
            [
                new AlamoSkippedChunk(AlamoParticleNoticeKind.UnreadByGame, 0, 0x7F),
                new AlamoSkippedChunk(AlamoParticleNoticeKind.IgnoredByGame, 0, 0x0100),
                new AlamoSkippedChunk(AlamoParticleNoticeKind.UnreadByGame, 1, 0x7F),
                new AlamoSkippedChunk(AlamoParticleNoticeKind.UnreadByGame, 2, 0x7F),
                new AlamoSkippedChunk(AlamoParticleNoticeKind.IgnoredByGame, 2, 0x0100),
                new AlamoSkippedChunk(AlamoParticleNoticeKind.UnreadByGame, null, 0x7E),
                new AlamoSkippedChunk(AlamoParticleNoticeKind.UnreadByGame, null, 0x7E)
            ]
        };

        var problems = ParticleNoticeProblems.For(system, Messages);

        Assert.Collection(problems,
            p =>
            {
                Assert.Equal(DiagnosticIds.PreviewParticleUnreadByGame, p.DiagnosticId);
                Assert.Equal("warning", p.Severity);
                Assert.Equal("Emitters 0-2 - chunk 0x7F is not read by the game and has no effect", p.Message);
            },
            p =>
            {
                Assert.Equal(DiagnosticIds.PreviewParticleIgnoredByGame, p.DiagnosticId);
                Assert.Equal("info", p.Severity);
                Assert.Equal(
                    "Emitters 0, 2 - the Particle Editor's link group is not read by the game and has no effect",
                    p.Message);
            },
            p =>
            {
                Assert.Equal(DiagnosticIds.PreviewParticleUnreadByGame, p.DiagnosticId);
                Assert.Equal("Particle system - chunk 0x7E is not read by the game and has no effect", p.Message);
            });
    }

    [Fact]
    public void For_GivesAShippedFileNothing()
    {
        Assert.Empty(ParticleNoticeProblems.For(new AlamoParticleContent("p_smoke", true, []), Messages));
    }

    [Fact]
    public void For_SaysASkippedPropertyOncePerFile_NamingEveryEmitterThatCarriesIt()
    {
        // #394: one line per emitter read as if the whole system were skipped, and a file with two
        // such properties on four emitters filled the list. One line per property per file, naming
        // the emitters as runs, in the order the properties first appear.
        var system = new AlamoParticleContent("p_test", true, [])
        {
            SkippedProperties =
            [
                new AlamoSkippedProperty(AlamoParticleNoticeKind.IgnoredByGame, 0, 0x06),
                new AlamoSkippedProperty(AlamoParticleNoticeKind.UnreadByGame, 0, 0x49),
                new AlamoSkippedProperty(AlamoParticleNoticeKind.IgnoredByGame, 1, 0x06),
                new AlamoSkippedProperty(AlamoParticleNoticeKind.IgnoredByGame, 2, 0x06),
                new AlamoSkippedProperty(AlamoParticleNoticeKind.UnreadByGame, 2, 0x49),
                new AlamoSkippedProperty(AlamoParticleNoticeKind.UnreadByGame, 5, 0x7F),
                new AlamoSkippedProperty(AlamoParticleNoticeKind.IgnoredByGame, 5, 0x06)
            ]
        };

        var problems = ParticleNoticeProblems.For(system, Messages);

        Assert.Collection(problems,
            p =>
            {
                Assert.Equal(DiagnosticIds.PreviewParticleIgnoredByGame, p.DiagnosticId);
                Assert.Equal("info", p.Severity);
                Assert.Equal("Emitters 0-2, 5 - property 0x06 is read past by the game and has no effect",
                    p.Message);
            },
            p =>
            {
                Assert.Equal(DiagnosticIds.PreviewParticleUnreadByGame, p.DiagnosticId);
                Assert.Equal("warning", p.Severity);
                Assert.Equal("Emitters 0, 2 - property 0x49 is not read by the game and has no effect",
                    p.Message);
            },
            p =>
            {
                Assert.Equal(DiagnosticIds.PreviewParticleUnreadByGame, p.DiagnosticId);
                Assert.Equal("Emitter 5 - property 0x7F is not read by the game and has no effect", p.Message);
            });
    }

    [Fact]
    public void For_NamesAnEmitterOnceHoweverOftenItCarriesTheProperty()
    {
        // A property repeated inside one emitter's block is still one emitter carrying it.
        var system = new AlamoParticleContent("p_test", true, [])
        {
            SkippedProperties =
            [
                new AlamoSkippedProperty(AlamoParticleNoticeKind.IgnoredByGame, 3, 0x06),
                new AlamoSkippedProperty(AlamoParticleNoticeKind.IgnoredByGame, 3, 0x06)
            ]
        };

        var problem = Assert.Single(ParticleNoticeProblems.For(system, Messages));

        Assert.Equal("Emitter 3 - property 0x06 is read past by the game and has no effect", problem.Message);
    }
}