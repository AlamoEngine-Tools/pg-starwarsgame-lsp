// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>
///     What a rename the Lua analyzer computed may touch: the project's own scripts. The analyzer
///     reads the lower layers and the engine stubs as library, and its rename can reach into them;
///     this server never edits a dependency, the game, or the stubs.
/// </summary>
internal static class LuaAnalyzerRename
{
    public static void EnsureWithinProject(WorkspaceEdit edit, IProjectLayerMap? layers)
    {
        if (layers is null || layers.Layers.Count == 0) return;
        var leaf = layers.Layers.Max(l => l.Rank);
        var roots = layers.Layers.Where(l => l.Rank == leaf)
            .SelectMany(l => l.ScriptRoots)
            .Select(r => Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .ToList();

        var uris = (edit.Changes?.Keys ?? [])
            .Concat(edit.DocumentChanges?
                .Where(c => c.IsTextDocumentEdit)
                .Select(c => c.TextDocumentEdit!.TextDocument.Uri) ?? []);
        foreach (var uri in uris)
        {
            var path = Path.GetFullPath(uri.GetFileSystemPath());
            if (!roots.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    $"Rename refused: It edits {Path.GetFileName(path)}, which is not one of this project's scripts");
        }
    }
}