// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Core.Workspace;

/// <summary>
///     A document's current text with its <see cref="ContentHasher" /> hash.
///     <see cref="FromOpenBuffer" /> distinguishes the live editor buffer from the saved file.
/// </summary>
public sealed record DocumentText(string Text, long ContentHash, bool FromOpenBuffer);

/// <summary>
///     The single "where does a document's current text come from" answer: the open editor buffer
///     when the document is tracked by the workspace host, the file on disk otherwise. Replaces
///     the hand-rolled host→disk fallbacks that used to live in every closed-file consumer
///     (rename builders, variant tag source, diagnostics revalidation).
/// </summary>
public interface IDocumentTextSource
{
    /// <summary>
    ///     Resolves the current text for <paramref name="canonicalUri" /> (must already be
    ///     normalized via <c>IFileHelper.NormalizeUri</c>). Returns null when the document is
    ///     neither open nor readable on disk.
    /// </summary>
    DocumentText? GetText(string canonicalUri);
}

public sealed class DocumentTextSource : IDocumentTextSource
{
    private readonly IFileHelper _fileHelper;
    private readonly IGameWorkspaceHost _host;
    private readonly ILogger<DocumentTextSource> _logger;

    public DocumentTextSource(IGameWorkspaceHost host, IFileHelper fileHelper,
        ILogger<DocumentTextSource> logger)
    {
        _host = host;
        _fileHelper = fileHelper;
        _logger = logger;
    }

    public DocumentText? GetText(string canonicalUri)
    {
        if (_host.TryGet(canonicalUri, out var doc))
            return new DocumentText(doc.Text, ContentHasher.Hash(doc.Text), true);

        var path = _fileHelper.FileUriToPath(canonicalUri);
        if (path is null || !_fileHelper.FileSystem.File.Exists(path))
            return null;

        try
        {
            var text = _fileHelper.FileSystem.File.ReadAllText(path);
            return new DocumentText(text, ContentHasher.Hash(text), false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not read '{Path}': {Message}", path, ex.Message);
            return null;
        }
    }
}