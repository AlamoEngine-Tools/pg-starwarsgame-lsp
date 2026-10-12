// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Analysis.Generated;

/// <summary>Lines inserted above an authored line of a script, for the analyzer only.</summary>
/// <param name="BeforeLine">The authored line (0-based) the lines go above.</param>
/// <param name="Lines">The inserted lines, without line breaks.</param>
public sealed record LuaAnnotationInsertion(int BeforeLine, IReadOnlyList<string> Lines);

/// <summary>
///     Lines of an authored document to lines of its generated twin, and back. Only whole lines are
///     ever inserted, so columns never move and the map is a sorted list of insertion points.
/// </summary>
public sealed class LuaLineMap
{
    // (authored line, lines inserted above it), ascending and merged per line.
    private readonly (int Line, int Count)[] _points;

    public LuaLineMap(IEnumerable<LuaAnnotationInsertion> insertions)
    {
        _points = insertions.Where(i => i.Lines.Count > 0)
            .GroupBy(i => i.BeforeLine)
            .Select(g => (g.Key, g.Sum(i => i.Lines.Count)))
            .OrderBy(p => p.Key)
            .ToArray();
    }

    public bool IsEmpty => _points.Length == 0;

    public int ToGenerated(int authoredLine)
    {
        var shift = 0;
        foreach (var (line, count) in _points)
        {
            if (line > authoredLine) break;
            shift += count;
        }

        return authoredLine + shift;
    }

    /// <summary>The authored line, or null for a line that exists only in the generated document.</summary>
    public int? ToAuthored(int generatedLine)
    {
        var shift = 0;
        foreach (var (line, count) in _points)
        {
            var firstInserted = line + shift;
            if (generatedLine < firstInserted) break;
            if (generatedLine < firstInserted + count) return null;
            shift += count;
        }

        return generatedLine - shift;
    }
}