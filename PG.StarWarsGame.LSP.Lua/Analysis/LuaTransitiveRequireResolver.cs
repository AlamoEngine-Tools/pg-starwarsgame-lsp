// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

internal static class LuaTransitiveRequireResolver
{
    public static IReadOnlySet<string> GetTransitiveDependencies(
        IReadOnlySet<string> directRequiredUris,
        IReadOnlyDictionary<string, DocumentIndex> allDocuments,
        IFileHelper fileHelper)
    {
        var visited = new HashSet<string>(directRequiredUris, StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(directRequiredUris);

        while (queue.Count > 0)
        {
            var uri = queue.Dequeue();
            if (!allDocuments.TryGetValue(uri, out var doc)) continue;
            if (doc.RequireArgs.IsDefaultOrEmpty) continue;

            foreach (var arg in doc.RequireArgs)
            {
                if (LuaRequireResolver.IsRelative(arg)) continue;
                var resolved = LuaRequireResolver.Resolve(arg, allDocuments, fileHelper);
                if (resolved is null) continue;
                if (visited.Add(resolved))
                    queue.Enqueue(resolved);
            }
        }

        return visited;
    }
}