// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

internal static class LuaRequireResolver
{
    // Returns true when the require argument uses relative path traversal (../../X, ./X, ../X).
    // Such requires cannot be reliably resolved from the static workspace index.
    public static bool IsRelative(string requireArg)
    {
        return requireArg.StartsWith("./", StringComparison.Ordinal) ||
               requireArg.StartsWith("../", StringComparison.Ordinal) ||
               requireArg.StartsWith(".\\", StringComparison.Ordinal) ||
               requireArg.StartsWith("..\\", StringComparison.Ordinal);
    }

    // Searches documents for any file whose path ends with the require target.
    // When multiple layers define the same file, the entry with the highest LayerRank wins,
    // consistent with how XML overrides work across mod layers.
    // Returns null without being an error when the require is relative (call IsRelative first).
    public static string? Resolve(
        string requireArg,
        IReadOnlyDictionary<string, DocumentIndex> documents,
        IFileHelper fileHelper)
    {
        if (IsRelative(requireArg))
            return null;

        var normalized = fileHelper.NormalizeGamePath(requireArg);
        var searchSuffix = "/" + normalized + ".lua";

        string? bestUri = null;
        var bestRank = int.MinValue;

        foreach (var (uri, doc) in documents)
        {
            if (!uri.EndsWith(searchSuffix, StringComparison.Ordinal)) continue;
            if (doc.LayerRank > bestRank)
            {
                bestRank = doc.LayerRank;
                bestUri = uri;
            }
        }

        return bestUri;
    }
}