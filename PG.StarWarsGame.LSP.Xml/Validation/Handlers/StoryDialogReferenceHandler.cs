// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Xml.Validation.Handlers;

/// <summary>
///     Cross-checks from a story event into the story-dialog language: a <c>Story_Dialog</c>
///     name must resolve to a script under the pgproj storyDialog directories (registry-scoped —
///     filename conventions play no part), and a <c>Story_Chapter</c> must exist in that script.
///     Inactive while the dialog language is off or no storyDialog directories are declared. The
///     scope is optional because it is implemented server-side — an Xml-only composition (tests,
///     other hosts) has none, and the handler is then inert.
/// </summary>
public sealed class StoryDialogReferenceHandler(IStoryDialogScope? scope = null)
    : XmlDiagnosticsHandler<StoryDialogRefFact>
{
    protected override IEnumerable<XmlDiagnosticResult> Handle(StoryDialogRefFact fact, DiagnosticsContext ctx)
    {
        if (scope is not { Enabled: true }) yield break;

        var resolved = scope.ResolveDialogFile(fact.DialogName);
        if (resolved is null)
        {
            yield return new XmlDiagnosticResult(XmlDiagnosticSeverity.Warning,
                $"Story dialog '{fact.DialogName}' does not resolve to a .txt script under the " +
                "project's storyDialog directories.");
            yield break;
        }

        if (fact.Chapter is not { } chapter) yield break;

        var chapters = scope.GetChapters(resolved);
        if (chapters.Contains(chapter)) yield break;

        var defined = chapters.Count > 0
            ? $"defined chapters: {string.Join(", ", chapters.Order())}"
            : "the file defines no chapters";
        yield return new XmlDiagnosticResult(XmlDiagnosticSeverity.Error,
            $"Chapter {chapter} is not defined in story dialog '{fact.DialogName}' ({defined}).",
            fact.ChapterLine >= 0 ? fact.ChapterLine : null);
    }
}
