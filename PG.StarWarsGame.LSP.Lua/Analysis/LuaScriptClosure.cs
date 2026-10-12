// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Lua.Analysis;

/// <summary>
///     The files of a script's Lua state: the libraries it requires, transitively. The game loads each
///     into the requiring script's own state, so what they define and set is the script's.
/// </summary>
internal static class LuaScriptClosure
{
    /// <summary>The required files, transitively - the script itself not among them.</summary>
    public static IReadOnlySet<string> RequiredBy(string scriptUri,
        IReadOnlyDictionary<string, DocumentIndex> documents, IFileHelper fileHelper)
    {
        if (!documents.TryGetValue(scriptUri, out var script) || script.RequireArgs.IsDefaultOrEmpty)
            return new HashSet<string>();
        var direct = script.RequireArgs
            .Select(arg => LuaRequireResolver.Resolve(arg, documents, fileHelper, scriptUri))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var closure = new HashSet<string>(
            LuaTransitiveRequireResolver.GetTransitiveDependencies(direct, documents, fileHelper),
            StringComparer.OrdinalIgnoreCase);
        closure.Remove(scriptUri);
        return closure;
    }

    /// <summary>The file of the closure named <paramref name="fileName" />, case-insensitively.</summary>
    public static IEnumerable<string> Named(IEnumerable<string> closure, string fileName, IFileHelper fileHelper)
    {
        return closure.Where(uri => string.Equals(Path.GetFileName(fileHelper.FileUriToPath(uri) ?? uri), fileName,
            StringComparison.OrdinalIgnoreCase));
    }
}