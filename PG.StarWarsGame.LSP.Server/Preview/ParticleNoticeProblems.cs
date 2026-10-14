// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Server.Preview;

/// <summary>
///     What a particle file carries that the game does not apply, as preview problems with ids.
/// </summary>
/// <remarks>
///     The reader classifies by what the game does with the data; this is the one place that
///     turns the classification into an id and a severity, so the preview panel and the
///     Problems view say the same thing about the same file. Data the game's reader has no case
///     for is a warning: nothing knows what it is. Known data the game deliberately leaves
///     unapplied - editor state - is information.
/// </remarks>
public static class ParticleNoticeProblems
{
    /// <summary>The problems for <paramref name="system" />, one per notice, in file order.</summary>
    public static IReadOnlyList<PreviewProblem> For(AlamoParticleContent system)
    {
        if (system.Notices.Count == 0)
            return [];

        var problems = new List<PreviewProblem>(system.Notices.Count);
        foreach (var notice in system.Notices)
            problems.Add(notice.Kind == AlamoParticleNoticeKind.IgnoredByGame
                ? new PreviewProblem(DiagnosticIds.PreviewParticleIgnoredByGame, "info", notice.Message)
                : new PreviewProblem(DiagnosticIds.PreviewParticleUnreadByGame, "warning", notice.Message));

        return problems;
    }
}
