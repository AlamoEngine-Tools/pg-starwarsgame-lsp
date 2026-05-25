// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Lua.Analysis;

internal static class LuaRequireResolver
{
    // Returns true when the require argument uses relative path traversal (../../X, ./X, ../X).
    // Such requires cannot be reliably resolved from the static workspace index.
    public static bool IsRelative(string requireArg) =>
        requireArg.StartsWith("./", StringComparison.Ordinal) ||
        requireArg.StartsWith("../", StringComparison.Ordinal) ||
        requireArg.StartsWith(".\\", StringComparison.Ordinal) ||
        requireArg.StartsWith("..\\", StringComparison.Ordinal);

    // Searches workspaceUris for any file whose path ends with the require target.
    // Returns the matching URI or null if not found.
    // Returns null without being an error when the require is relative (call IsRelative first).
    public static string? Resolve(string requireArg, IEnumerable<string> workspaceUris)
    {
        if (IsRelative(requireArg))
            return null;

        var normalized = requireArg.Replace('\\', '/').ToLowerInvariant();
        var searchSuffix = "/" + normalized + ".lua";

        foreach (var uri in workspaceUris)
        {
            var normalizedUri = uri.Replace('\\', '/').ToLowerInvariant();
            if (normalizedUri.EndsWith(searchSuffix, StringComparison.Ordinal))
                return uri;
        }

        return null;
    }
}
