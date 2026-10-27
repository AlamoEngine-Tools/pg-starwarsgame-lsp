// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

internal static class LuaFileClassifier
{
    private const string LibrarySegment = "/library/";

    public static bool IsLibraryUri(string uri)
    {
        return uri.Contains(LibrarySegment, StringComparison.OrdinalIgnoreCase);
    }

    // The directories whose scripts a host attaches per instance and releases again - objects, the
    // free store, plans, interventions, scoring, story - so their state can be pooled and reused.
    // Evaluators and the libraries are loaded once and never handed to another instance.
    private static readonly string[] s_instanceDirectories =
    [
        "/data/scripts/gameobject/", "/data/scripts/freestore/", "/data/scripts/ai/", "/data/scripts/interventions/",
        "/data/scripts/miscellaneous/", "/data/scripts/story/"
    ];

    public static bool IsInstanceScriptUri(string uri)
    {
        var normalized = GamePaths.Forward(Uri.UnescapeDataString(uri));
        return s_instanceDirectories.Any(d => normalized.Contains(d, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlySet<string> GetSharedUris(
        IReadOnlyDictionary<string, DocumentIndex> documents,
        IFileHelper fileHelper)
    {
        var shared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var uri in documents.Keys)
            if (IsLibraryUri(uri))
                shared.Add(uri);

        foreach (var (_, doc) in documents)
        {
            if (doc.RequireArgs.IsDefaultOrEmpty) continue;
            foreach (var arg in doc.RequireArgs)
            {
                if (LuaRequireResolver.IsRelative(arg)) continue;
                var resolved = LuaRequireResolver.Resolve(arg, documents, fileHelper);
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