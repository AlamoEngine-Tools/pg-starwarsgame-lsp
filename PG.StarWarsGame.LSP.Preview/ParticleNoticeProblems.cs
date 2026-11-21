// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Messages;

namespace PG.StarWarsGame.LSP.Preview;

/// <summary>
///     What a particle file carries that the game does not apply, as preview problems with ids.
/// </summary>
/// <remarks>
///     The reader classifies by what the game does with the data; this is the one place that
///     turns the classification into an id and a severity, so the preview panel and the
///     Problems view say the same thing about the same file. Data the game's reader has no case
///     for is a warning: nothing knows what it is. Known data the game deliberately leaves
///     unapplied - editor state - is information.
///     <para>
///         Each skipped chunk and property is said once per file, naming the emitters that carry it
///         (#394). One line per emitter, ending "skipped", read as if the whole system were skipped,
///         and a file whose four emitters carried two such properties gave eight lines. What the
///         game does with each is measured on its reader: 0x06 is read past by its length; a chunk
///         or property id its switch has no case for - the Particle Editor's link group among them -
///         is opened, left unread and closed. None of them is applied.
///     </para>
/// </remarks>
public static class ParticleNoticeProblems
{
    /// <summary>
    ///     The problems for <paramref name="system" />: one per skipped chunk, then one per skipped
    ///     property, each in the order it first appears in the file.
    /// </summary>
    public static IReadOnlyList<PreviewProblem> For(AlamoParticleContent system, IMessageCatalog messages)
    {
        var problems = new List<PreviewProblem>();

        foreach (var chunk in system.SkippedChunks.GroupBy(s => (s.Kind, AtSystem: s.Emitter is null, s.Chunk)))
        {
            var hex = chunk.Key.Chunk.ToString("X", CultureInfo.InvariantCulture);

            if (chunk.Key.AtSystem)
            {
                problems.Add(Problem(chunk.Key.Kind, messages.Format("ParticleNotice_SystemChunkNotRead", hex)));
                continue;
            }

            var which = Emitters(chunk.Select(s => s.Emitter!.Value), messages);
            problems.Add(Problem(chunk.Key.Kind, chunk.Key.Chunk == AlamoSkippedChunk.ParticleEditorLinkGroup
                ? messages.Format("ParticleNotice_LinkGroupNotRead", which)
                : messages.Format("ParticleNotice_ChunkNotRead", which, hex)));
        }

        foreach (var property in system.SkippedProperties.GroupBy(s => (s.Kind, s.Property)))
        {
            var which = Emitters(property.Select(s => s.Emitter), messages);
            var hex = property.Key.Property.ToString("X2", CultureInfo.InvariantCulture);

            problems.Add(Problem(property.Key.Kind, property.Key.Kind == AlamoParticleNoticeKind.IgnoredByGame
                ? messages.Format("ParticleNotice_PropertyReadPast", which, hex)
                : messages.Format("ParticleNotice_PropertyNotRead", which, hex)));
        }

        return problems;
    }

    /// <summary>"Emitter 3", or "Emitters 0-2, 5" for several.</summary>
    private static string Emitters(IEnumerable<int> indices, IMessageCatalog messages)
    {
        var emitters = indices.Distinct().Order().ToList();
        return emitters.Count == 1
            ? messages.Format("ParticleNotice_Emitter", emitters[0])
            : messages.Format("ParticleNotice_Emitters", Runs(emitters));
    }

    private static PreviewProblem Problem(AlamoParticleNoticeKind kind, string message)
    {
        return kind == AlamoParticleNoticeKind.IgnoredByGame
            ? new PreviewProblem(DiagnosticIds.PreviewParticleIgnoredByGame, "info", message)
            : new PreviewProblem(DiagnosticIds.PreviewParticleUnreadByGame, "warning", message);
    }

    /// <summary>Ascending indices as runs: <c>0-2, 5</c>.</summary>
    private static string Runs(IReadOnlyList<int> indices)
    {
        var runs = new List<string>();
        for (var start = 0; start < indices.Count;)
        {
            var end = start;
            while (end + 1 < indices.Count && indices[end + 1] == indices[end] + 1)
                end++;

            runs.Add(end == start
                ? indices[start].ToString(CultureInfo.InvariantCulture)
                : $"{indices[start].ToString(CultureInfo.InvariantCulture)}-{indices[end].ToString(CultureInfo.InvariantCulture)}");
            start = end + 1;
        }

        return string.Join(", ", runs);
    }
}