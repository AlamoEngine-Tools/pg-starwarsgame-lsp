// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

internal enum LuaFileTier
{
    Library,
    Dependency,
    Standalone
}

internal static class LuaFileClassifier
{
    private const string LibrarySegment = "/library/";

    public static bool IsLibraryUri(string uri) =>
        uri.Contains(LibrarySegment, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlySet<string> GetSharedUris(
        IReadOnlyDictionary<string, DocumentIndex> documents,
        IFileHelper fileHelper)
    {
        var shared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var uri in documents.Keys)
            if (IsLibraryUri(uri))
                shared.Add(uri);

        var workspaceUris = documents.Keys;
        foreach (var (_, doc) in documents)
        {
            if (doc.RequireArgs.IsDefaultOrEmpty) continue;
            foreach (var arg in doc.RequireArgs)
            {
                if (LuaRequireResolver.IsRelative(arg)) continue;
                var resolved = LuaRequireResolver.Resolve(arg, workspaceUris, fileHelper);
                if (resolved is not null)
                    shared.Add(resolved);
            }
        }

        return shared;
    }

    public static LuaFileTier GetTier(
        string uri,
        IReadOnlyDictionary<string, DocumentIndex> documents,
        IFileHelper fileHelper)
    {
        if (IsLibraryUri(uri)) return LuaFileTier.Library;
        var shared = GetSharedUris(documents, fileHelper);
        return shared.Contains(uri) ? LuaFileTier.Dependency : LuaFileTier.Standalone;
    }
}
