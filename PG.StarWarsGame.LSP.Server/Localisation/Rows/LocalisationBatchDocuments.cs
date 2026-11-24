// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Localisation;
using PG.StarWarsGame.LSP.Server.Editing;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>One localisation file a batch may write: where it is and the text the batch is composed from.</summary>
/// <param name="Version">The open document's version; null when the saved file was read.</param>
public sealed record LocalisationBatchDocument(
    string FilePath,
    string Uri,
    string Extension,
    string Text,
    int? Version);

/// <summary>
///     Opens the files a localisation batch is about for writing, in the way that is the same for
///     the grid and the credits editor: every file is checked before any is composed, because a
///     batch is one edit and must not be found half refusable on the way.
/// </summary>
public sealed class LocalisationBatchDocuments(
    IFileHelper fileHelper,
    IProjectLayerMap layers,
    IDocumentTextSource textSource,
    IMessageCatalog messages)
{
    /// <summary>
    ///     The files of <paramref name="subject" /> with their texts, or the reason the batch is
    ///     refused: no file named, a file that is gone, a compiled DAT, a file of a layer the
    ///     project does not own, or a file that changed since the editor read it.
    /// </summary>
    /// <returns>Null when every file was opened; otherwise why the batch is refused.</returns>
    public string? Open(EditBatchSubject subject, out IReadOnlyList<LocalisationBatchDocument> opened)
    {
        var documents = new List<LocalisationBatchDocument>();
        opened = documents;

        var named = subject.Documents ?? [];
        if (named.Count == 0 || named.Any(d => string.IsNullOrWhiteSpace(d.FilePath)))
            return messages.Format("LocApplyBatch_NoProjectFilePathProvided");

        var fs = fileHelper.FileSystem;
        foreach (var document in named)
        {
            if (!fs.File.Exists(document.FilePath))
                return messages.Format("LocApplyBatch_FileNotFound", document.FilePath);

            // A DAT is what a build makes of the project, not a file an editor writes: it has no
            // text for an edit to be made in.
            var extension = fs.Path.GetExtension(document.FilePath).ToLowerInvariant();
            if (extension == ".dat")
                return messages.Format("LocApplyBatch_DatIsBuilt", fs.Path.GetFileName(document.FilePath));

            // Before anything is composed: a dependency's file is listed beside the project's own so
            // the grid can show what the layers below say, not so the project can rewrite it (#276).
            if (LocalisationLayerGuard.ReadOnlyObjection(layers, fileHelper, document.FilePath, messages)
                is { } readOnly)
                return readOnly;

            var uri = fileHelper.NormalizeUri(document.FilePath);
            if (textSource.GetText(uri) is not { } text)
                return messages.Format("LocApplyBatch_FileNotFound", document.FilePath);

            // The batch was staged against what the editor last read. A file that has moved since
            // would take an edit addressed to other rows.
            if (LocalisationConcurrencyGuard.Check(text.Text, document.ContentHash, messages) is { } stale)
                return stale.Error;

            documents.Add(new LocalisationBatchDocument(
                document.FilePath, uri, extension, text.Text, text.FromOpenBuffer ? text.Version : null));
        }

        return null;
    }

    /// <summary>The file a validation reads: the first of the set, which is the one the editor was opened on.</summary>
    /// <returns>Null when it exists; otherwise the message that it does not, built from <paramref name="notFoundKey" />.</returns>
    public string? First(EditBatchSubject subject, string notFoundKey, out string filePath)
    {
        filePath = subject.Documents?.FirstOrDefault()?.FilePath ?? string.Empty;
        return string.IsNullOrWhiteSpace(filePath) || !fileHelper.FileSystem.File.Exists(filePath)
            ? messages.Format(notFoundKey, filePath)
            : null;
    }

    /// <summary>Each file of the batch with the content hash it has once the batch is written.</summary>
    public static IReadOnlyList<EditBatchDocument> Written(
        IReadOnlyList<LocalisationBatchDocument> documents, IReadOnlyList<string> texts)
    {
        return documents
            .Select((d, i) => new EditBatchDocument(d.FilePath, LocalisationContentHash.Compute(texts[i])))
            .ToList();
    }

    /// <summary>The edit that writes the files whose text the batch changed.</summary>
    public static OmniSharp.Extensions.LanguageServer.Protocol.Models.WorkspaceEdit? Edit(
        IReadOnlyList<LocalisationBatchDocument> documents, IReadOnlyList<string> texts)
    {
        var changed = documents
            .Select((d, i) => new EditBatchFileChange(d.Uri, d.Text, texts[i], d.Version))
            .Where(c => !string.Equals(c.Original, c.Current, StringComparison.Ordinal))
            .ToList();
        return changed.Count == 0 ? null : EditBatchEdits.Build(changed);
    }
}