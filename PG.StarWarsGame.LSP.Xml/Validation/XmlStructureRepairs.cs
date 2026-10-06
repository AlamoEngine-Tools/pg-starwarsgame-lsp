// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using PG.StarWarsGame.LSP.Core.Diagnostics;

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>Applies structural repairs: one at a time, or every repairable finding in a file.</summary>
public static class XmlStructureRepairs
{
    /// <summary>
    ///     Upper bound on fix-all passes. The game's reader stops at its first error, so a file with
    ///     several lost-file defects needs one pass each; anything left after this many is reported
    ///     rather than chased.
    /// </summary>
    public const int DefaultIterationCap = 50;

    /// <summary>The text with <paramref name="repair" />'s edits applied.</summary>
    public static string Apply(string text, XmlRepair repair)
    {
        return Apply(text, repair.Edits);
    }

    /// <summary>
    ///     Repairs, re-reads and repeats until no included finding has a repair, or the cap is
    ///     reached. Each pass applies every repair whose edits do not overlap one already taken.
    /// </summary>
    public static (string Text, int Iterations) FixAll(
        string text, IXmlStructuralValidator validator, Func<XmlStrictnessCategory, bool> include,
        int iterationCap = DefaultIterationCap)
    {
        var iterations = 0;
        while (iterations < iterationCap)
        {
            var taken = new List<XmlTextEdit>();
            foreach (var finding in validator.Validate(text))
            {
                if (finding.Repair is not { } repair || !include(finding.Category)) continue;
                if (repair.Edits.Any(e => taken.Any(t => Overlaps(e, t)))) continue;
                taken.AddRange(repair.Edits);
            }

            if (taken.Count == 0) break;
            text = Apply(text, taken);
            iterations++;
        }

        return (text, iterations);
    }

    private static string Apply(string text, IEnumerable<XmlTextEdit> edits)
    {
        // Back to front, so earlier offsets stay valid; at one offset, the later-listed edit first.
        var ordered = edits.Select((e, i) => (Edit: e, Index: i))
            .OrderByDescending(x => x.Edit.Start)
            .ThenByDescending(x => x.Index);
        var sb = new StringBuilder(text);
        foreach (var (edit, _) in ordered)
        {
            sb.Remove(edit.Start, edit.Length);
            sb.Insert(edit.Start, edit.NewText);
        }

        return sb.ToString();
    }

    // Two pure insertions at one offset do not overlap; anything sharing a character does.
    private static bool Overlaps(XmlTextEdit a, XmlTextEdit b)
    {
        if (a.Length == 0 && b.Length == 0) return false;
        return a.Start < b.Start + Math.Max(1, b.Length) && b.Start < a.Start + Math.Max(1, a.Length);
    }
}
