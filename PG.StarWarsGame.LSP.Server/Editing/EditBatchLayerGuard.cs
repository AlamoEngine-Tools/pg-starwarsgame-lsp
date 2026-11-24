// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Editing;

/// <summary>
///     The last word on where a batch may write: nothing below the project.
///     <para>
///         A dependency's files and the game's are listed beside the project's own so an editor can
///         show what the layers below say. They are not the project's to change. Each composer
///         refuses such a file where it can say which staged change asked for it; this is held
///         against the composed edit itself, whatever composed it, so the rule does not depend on
///         every composer remembering it.
///     </para>
/// </summary>
public sealed class EditBatchLayerGuard(IProjectLayerMap layers, IFileHelper fileHelper, IMessageCatalog messages)
{
    /// <summary>
    ///     Why <paramref name="edit" /> may not be written, or null when every file it changes or
    ///     creates is the project's own - or belongs to no layer at all, as before a project is loaded.
    /// </summary>
    public string? Objection(WorkspaceEdit? edit)
    {
        if (edit?.DocumentChanges is null || layers.Layers.Count == 0) return null;

        var leaf = layers.Layers.Max(l => l.Rank);
        foreach (var change in edit.DocumentChanges)
        {
            var target = change.IsTextDocumentEdit ? change.TextDocumentEdit!.TextDocument.Uri
                : change.IsCreateFile ? change.CreateFile!.Uri
                : null;
            if (target is null) continue;

            var uri = fileHelper.NormalizeUri(target.ToString());
            var rank = layers.GetRank(uri);
            if (rank >= leaf) continue;

            return messages.Format(
                "EditBatch_ReadOnlyLayer",
                fileHelper.FileUriToPath(uri) ?? uri,
                layers.GetLayerName(rank) ?? messages.Format("EditBatch_ALowerLayer"));
        }

        return null;
    }
}