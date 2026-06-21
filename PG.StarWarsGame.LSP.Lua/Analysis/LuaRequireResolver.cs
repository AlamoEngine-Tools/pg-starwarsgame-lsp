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
    // For relative requires (./foo, ../bar), callerUri must be provided; without it returns null.
    public static string? Resolve(
        string requireArg,
        IReadOnlyDictionary<string, DocumentIndex> documents,
        IFileHelper fileHelper,
        string? callerUri = null)
    {
        if (IsRelative(requireArg))
            return callerUri is null ? null : ResolveRelative(requireArg, callerUri, documents, fileHelper);

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

    private static string? ResolveRelative(
        string requireArg,
        string callerUri,
        IReadOnlyDictionary<string, DocumentIndex> documents,
        IFileHelper fileHelper)
    {
        try
        {
            // URI relative resolution: "./sibling" or "../lib" resolved against the caller's URI.
            // The System.Uri constructor handles multi-level traversal (../../foo) correctly.
            var normalizedArg = requireArg.Replace('\\', '/') + ".lua";
            var baseUri = new Uri(callerUri);
            var resolved = new Uri(baseUri, normalizedArg);
            var resolvedNormalized = fileHelper.NormalizeUri(resolved.ToString());
            return documents.ContainsKey(resolvedNormalized) ? resolvedNormalized : null;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
}