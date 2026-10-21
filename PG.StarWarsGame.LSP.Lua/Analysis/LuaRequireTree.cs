// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

/// <summary>
///     What one script instance holds. The game runs every script instance in a Lua state of its own,
///     and a required library runs inside the requiring script's state, so the state is the script
///     plus its <c>require</c> tree - resolved across the layers, the leaf first, as the engine does.
/// </summary>
/// <param name="Files">The script, then every file it requires, transitively, each once.</param>
/// <param name="Unresolved">The modules required that no layer ships, each once, as first written.</param>
public sealed record LuaRequireTree(IReadOnlyList<string> Files, IReadOnlyList<string> Unresolved)
{
    public static LuaRequireTree Of(string uri, IReadOnlyDictionary<string, DocumentIndex> documents,
        IFileHelper fileHelper)
    {
        var files = new List<string> { uri };
        var held = new HashSet<string>(DocumentUris.Comparer) { uri };
        var unresolved = new List<string>();
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < files.Count; i++)
        {
            if (!documents.TryGetValue(files[i], out var current) || current.RequireArgs.IsDefaultOrEmpty) continue;
            foreach (var arg in current.RequireArgs)
                if (LuaRequireResolver.Resolve(arg, documents, fileHelper, current.DocumentUri) is { } required)
                {
                    if (held.Add(required)) files.Add(required);
                }
                else if (missing.Add(arg))
                {
                    unresolved.Add(arg);
                }
        }

        return new LuaRequireTree(files, unresolved);
    }
}
