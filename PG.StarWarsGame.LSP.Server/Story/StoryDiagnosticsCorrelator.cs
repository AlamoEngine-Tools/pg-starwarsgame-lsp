// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     Maps XML diagnostics for one thread onto graph nodes and param slots. Shared by
///     <see cref="GetStoryDiagnosticsHandler" /> (over committed buffer text, node ids from the
///     model graph) and <see cref="ValidateStoryCommandBatchHandler" /> (over the staged working
///     text, node ids computed by name) - the range-containment correlation is identical either way.
/// </summary>
internal static class StoryDiagnosticsCorrelator
{
    /// <summary>
    ///     Correlates each diagnostic to the event whose range contains it (and to the event/reward
    ///     param slot within it), tagging it with the node id from <paramref name="nodeIdOf" />.
    /// </summary>
    public static void Correlate(
        string uri,
        IReadOnlyList<StoryEvent> events,
        IEnumerable<Diagnostic> diagnostics,
        Func<StoryEvent, string?> nodeIdOf,
        List<StoryDiagnosticDto> into)
    {
        foreach (var diagnostic in diagnostics)
        {
            var line = diagnostic.Range.Start.Line;
            var column = diagnostic.Range.Start.Character;
            // Column-aware containment: several events can share a line (single-line files).
            var storyEvent = events.FirstOrDefault(e => Contains(e.Range, line, column));

            string? side = null;
            int? position = null;
            if (storyEvent is not null)
            {
                var slot = storyEvent.EventParams.FirstOrDefault(p => Contains(p.Range, line, column));
                if (slot is not null)
                {
                    side = "event";
                }
                else
                {
                    slot = storyEvent.RewardParams.FirstOrDefault(p => Contains(p.Range, line, column));
                    if (slot is not null) side = "reward";
                }

                position = slot?.Position;
            }

            into.Add(new StoryDiagnosticDto(
                storyEvent is not null ? nodeIdOf(storyEvent) : null,
                side, position,
                SeverityOf(diagnostic),
                diagnostic.Message,
                uri, line, column));
        }
    }

    private static bool Contains(StorySourceRange range, int line, int column)
    {
        if (range.StartLine < 0) return false;
        var afterStart = line > range.StartLine || (line == range.StartLine && column >= range.StartColumn);
        var beforeEnd = line < range.EndLine || (line == range.EndLine && column <= range.EndColumn);
        return afterStart && beforeEnd;
    }

    private static string SeverityOf(Diagnostic diagnostic)
    {
        return diagnostic.Severity switch
        {
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Information => "info",
            DiagnosticSeverity.Hint => "info",
            _ => "error" // Error, or unset (LSP treats missing severity as error)
        };
    }
}