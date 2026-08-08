// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Commits a staged batch of position-addressed credits edits. See
///     <see cref="ApplyLocalisationBatchHandlerBase{TCommand,TResult}" /> for everything this shares
///     with the translation side; what is left here is how a credits edit is addressed.
/// </summary>
public sealed class ApplyCreditsBatchHandler
    : ApplyLocalisationBatchHandlerBase<LocEditCommandDto, ApplyCreditsBatchResult>,
        IJsonRpcRequestHandler<ApplyCreditsBatchParams, ApplyCreditsBatchResult>
{
    private readonly ILocalisationDocumentEditor _editor;

    public ApplyCreditsBatchHandler(
        ILocalisationDocumentEditor editor,
        IModProjectReloadService reloadService,
        IFileHelper fileHelper,
        ILogger<ApplyCreditsBatchHandler> logger,
        ILspConfigurationProvider config,
        ILocalisationWriteLedger writeLedger)
        : base(reloadService, fileHelper, logger, config, writeLedger)
    {
        _editor = editor;
    }

    protected override string FileKind => "credits file";

    public Task<ApplyCreditsBatchResult> Handle(
        ApplyCreditsBatchParams request, CancellationToken ct)
    {
        return ApplyBatchAsync(
            request.ProjectFilePath, request.ExpectedContentHash, request.Commands, ct);
    }

    protected override Task<LocalisationEditResult> ApplyAsync(
        string filePath, IReadOnlyList<LocEditCommandDto> commands, CancellationToken ct)
    {
        return _editor.ApplyToFileAsync(filePath, commands, ct);
    }

    protected override ApplyCreditsBatchResult Succeeded(string newContentHash)
    {
        return new ApplyCreditsBatchResult(true, NewContentHash: newContentHash);
    }

    protected override ApplyCreditsBatchResult Failed(int? failedIndex, string error)
    {
        return new ApplyCreditsBatchResult(false, failedIndex, error);
    }
}
