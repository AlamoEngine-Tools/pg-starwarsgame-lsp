// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Commits a staged batch of key-addressed translation edits. All or nothing: if any command
///     fails the file is left untouched and the client keeps its queue, so a partial save can never
///     leave the grid and the file disagreeing about what was written.
///     <para>
///         Everything this shares with the credits side is in
///         <see cref="ApplyLocalisationBatchHandlerBase{TCommand,TResult}" />. What is left here is
///         the resolution of keys to rows, which is the whole reason the two are separate endpoints.
///     </para>
/// </summary>
public sealed class ApplyTranslationBatchHandler
    : ApplyLocalisationBatchHandlerBase<LocKeyedCommandDto, ApplyTranslationBatchResult>,
        IJsonRpcRequestHandler<ApplyTranslationBatchParams, ApplyTranslationBatchResult>
{
    private readonly ILocalisationDocumentEditor _editor;

    public ApplyTranslationBatchHandler(
        ILocalisationDocumentEditor editor,
        IModProjectReloadService reloadService,
        IFileHelper fileHelper,
        ILogger<ApplyTranslationBatchHandler> logger,
        ILspConfigurationProvider config,
        ILocalisationWriteLedger writeLedger)
        : base(reloadService, fileHelper, logger, config, writeLedger)
    {
        _editor = editor;
    }

    protected override string FileKind => "localisation file";

    public Task<ApplyTranslationBatchResult> Handle(
        ApplyTranslationBatchParams request, CancellationToken ct)
    {
        return ApplyBatchAsync(
            request.ProjectFilePath, request.ExpectedContentHash, request.Commands, ct);
    }

    protected override Task<LocalisationEditResult> ApplyAsync(
        string filePath, IReadOnlyList<LocKeyedCommandDto> commands, CancellationToken ct)
    {
        return _editor.ApplyKeyedToFileAsync(filePath, commands, ct);
    }

    protected override ApplyTranslationBatchResult Succeeded(string newContentHash)
    {
        return new ApplyTranslationBatchResult(true, NewContentHash: newContentHash);
    }

    protected override ApplyTranslationBatchResult Failed(int? failedIndex, string error)
    {
        return new ApplyTranslationBatchResult(false, failedIndex, error);
    }
}
