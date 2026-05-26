// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

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

    // Searches workspaceUris for any file whose path ends with the require target.
    // workspaceUris must already be normalized (i.e., from GameIndex.Documents.Keys).
    // Returns the matching URI or null if not found.
    // Returns null without being an error when the require is relative (call IsRelative first).
    public static string? Resolve(string requireArg, IEnumerable<string> workspaceUris, IFileHelper fileHelper)
    {
        if (IsRelative(requireArg))
            return null;

        var normalized = fileHelper.NormalizeGamePath(requireArg);
        var searchSuffix = "/" + normalized + ".lua";

        foreach (var uri in workspaceUris)
            if (uri.EndsWith(searchSuffix, StringComparison.Ordinal))
                return uri;

        return null;
    }
}