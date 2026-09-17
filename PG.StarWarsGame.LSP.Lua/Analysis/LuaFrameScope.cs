// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Loretta.CodeAnalysis;
using PG.StarWarsGame.LSP.Lua.Completion;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

/// <summary>A local variable or parameter that is in scope at a position.</summary>
public sealed record LuaFrameLocal(string Name, bool IsParameter);

/// <summary>
///     The locals and parameters visible at a position, from the same walk completion uses for
///     its local-scope entries. Globals are deliberately not included: a debugger reads those by
///     name from the running game, and only locals need the name list a static parse can supply.
/// </summary>
public static class LuaFrameScope
{
    /// <param name="tree">The parsed document.</param>
    /// <param name="text">The text the tree was parsed from.</param>
    /// <param name="line">Zero-based line.</param>
    /// <param name="character">Zero-based column; locals whose declaration ends after this position are not in scope yet.</param>
    public static IReadOnlyList<LuaFrameLocal> LocalsAt(SyntaxTree tree, string text, int line, int character)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(text);

        var root = tree.GetRoot();
        var offset = LuaLocalScopeCollector.ComputeOffset(text, line, character);
        var entries = new List<ScopeEntry>();
        LuaLocalScopeCollector.CollectLocals(root, offset, entries);
        LuaLocalScopeCollector.CollectParameters(root, offset, entries);
        return entries
            .Select(e => new LuaFrameLocal(e.Name, e.Kind == ScopeEntryKind.Parameter))
            .ToList();
    }
}