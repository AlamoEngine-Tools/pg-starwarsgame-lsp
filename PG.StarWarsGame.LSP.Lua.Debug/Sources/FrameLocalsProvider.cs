// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Lua.Analysis;
using PG.StarWarsGame.LSP.Lua.Parsing;

namespace PG.StarWarsGame.LSP.Lua.Debug.Sources;

/// <inheritdoc />
public sealed class FrameLocalsProvider : IFrameLocalsProvider
{
    public IReadOnlyList<LuaFrameLocal> LocalsAt(string text, int line)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (line < 1)
            return [];

        // The start of the line: a local declared on the current line is not active until the
        // statement completes, which matches what the game's frame lookup would find.
        var document = ParsedLuaDocument.Parse(text);
        var raw = LuaFrameScope.LocalsAt(document.Tree, text, line - 1, 0);

        // A name declared twice in scope resolves to its innermost declaration on the game side
        // as well, so the last occurrence wins here.
        var byName = new Dictionary<string, LuaFrameLocal>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var local in raw)
        {
            if (byName.ContainsKey(local.Name))
                order.Remove(local.Name);
            byName[local.Name] = local;
            order.Add(local.Name);
        }

        return order.Select(name => byName[name]).ToList();
    }
}