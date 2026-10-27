// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Lua.Analysis;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>
///     What a rename the Lua analyzer computed may touch: the project's own scripts. The analyzer
///     reads the lower layers and the engine stubs as library, and its rename can reach into them;
///     this server never edits a dependency, the game, or the stubs.
/// </summary>
internal static class LuaAnalyzerRename
{
    /// <summary>
    ///     The edits within the rename's scope from <paramref name="from" />: the game has one state
    ///     per script, joined where a shared file names the global (#249). The analyzer's edit is its
    ///     list of files naming it.
    /// </summary>
    public static WorkspaceEdit KeepWithinState(WorkspaceEdit edit, LuaSandbox sandbox, string from)
    {
        var naming = (edit.Changes?.Keys ?? []).Concat(edit.DocumentChanges?
                .Where(c => c.IsTextDocumentEdit)
                .Select(c => c.TextDocumentEdit!.TextDocument.Uri) ?? [])
            .Select(u => u.ToString());
        var inScope = sandbox.RenameScope(from, naming);
        return edit with
        {
            Changes = edit.Changes?
                .Where(c => inScope(c.Key.ToString()))
                .ToDictionary(c => c.Key, c => c.Value),
            DocumentChanges = edit.DocumentChanges is null
                ? null
                : new Container<WorkspaceEditDocumentChange>(edit.DocumentChanges.Where(c =>
                    !c.IsTextDocumentEdit || inScope(c.TextDocumentEdit!.TextDocument.Uri.ToString())))
        };
    }

    public static void EnsureWithinProject(WorkspaceEdit edit, IProjectLayerMap layers,
        IMessageCatalog? messages = null)
    {
        messages ??= ResxMessageCatalog.Server("en");
        if (layers.Layers.Count == 0) return;
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
                    messages.Format("LuaAnalyzerRename_RenameRefusedEditsWhichNot", Path.GetFileName(path)));
        }
    }
}