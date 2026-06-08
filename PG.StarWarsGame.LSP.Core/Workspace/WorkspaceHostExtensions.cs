// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Workspace;

public static class WorkspaceHostExtensions
{
    /// <summary>
    ///     Returns the open document from the workspace host, or — when the client never sent a
    ///     <c>didOpen</c> for it (e.g. the vscode-languageclient restored-document race, where the
    ///     editor restored the tab before the language client subscribed) — falls back to the
    ///     on-disk version so request handlers (hover, inlay hints, completion, …) work regardless
    ///     of client document sync. Unsaved editor edits are only reflected once the client actually
    ///     syncs the document via <c>didOpen</c>/<c>didChange</c>; until then the saved file is used.
    /// </summary>
    public static bool TryGetOrReadFromDisk(this IGameWorkspaceHost host, IFileHelper fileHelper,
        string normalizedUri, out TrackedDocument doc)
    {
        if (host.TryGet(normalizedUri, out doc))
            return true;

        var path = fileHelper.FileUriToPath(normalizedUri);
        if (path is not null && fileHelper.FileSystem.File.Exists(path))
        {
            doc = new TrackedDocument(normalizedUri, fileHelper.FileSystem.File.ReadAllText(path), 0);
            return true;
        }

        doc = null!;
        return false;
    }
}