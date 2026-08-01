// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Commits a staged batch of key-addressed translation edits.
///     <para>
///         Writes to disk rather than sending <c>workspace/applyEdit</c>, for the same reason the
///         credits path does: the whole localisation read path is disk-based, so an applyEdit would
///         leave the file unsaved and make every later read stale.
///     </para>
/// </summary>
public sealed class ApplyTranslationBatchHandler
    : IJsonRpcRequestHandler<ApplyTranslationBatchParams, ApplyTranslationBatchResult>
{
    private readonly ILspConfigurationProvider _config;
    private readonly ILocalisationDocumentEditor _editor;
    private readonly IFileHelper _fileHelper;
    private readonly ILogger<ApplyTranslationBatchHandler> _logger;
    private readonly IModProjectReloadService _reloadService;

    public ApplyTranslationBatchHandler(
        ILocalisationDocumentEditor editor,
        IModProjectReloadService reloadService,
        IFileHelper fileHelper,
        ILogger<ApplyTranslationBatchHandler> logger,
        ILspConfigurationProvider config)
    {
        _editor = editor;
        _reloadService = reloadService;
        _fileHelper = fileHelper;
        _logger = logger;
        _config = config;
    }

    public async Task<ApplyTranslationBatchResult> Handle(
        ApplyTranslationBatchParams request, CancellationToken ct)
    {
        if (!_config.Current.Features.Tools.Localisation)
            return Fail(LocalisationFeatureDisabled.Message);

        if (string.IsNullOrWhiteSpace(request.ProjectFilePath))
            return Fail("No project file path provided.");

        var fs = _fileHelper.FileSystem;
        if (!fs.File.Exists(request.ProjectFilePath))
            return Fail($"File not found: {request.ProjectFilePath}");

        // Checked once for the whole batch, before anything is composed: the batch is atomic, so
        // there is no point discovering a stale file halfway through.
        if (await LocalisationConcurrencyGuard.CheckAsync(
                fs, request.ProjectFilePath, request.ExpectedContentHash, ct) is { } stale)
            return Fail(stale.Error!);

        if (request.Commands.Count == 0)
        {
            var unchanged = await fs.File.ReadAllTextAsync(request.ProjectFilePath, ct);
            return new ApplyTranslationBatchResult(true,
                NewContentHash: LocalisationContentHash.Compute(unchanged));
        }

        LocalisationEditResult result;
        try
        {
            result = await _editor.ApplyKeyedToFileAsync(
                request.ProjectFilePath, request.Commands, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write localisation file '{Path}'.", request.ProjectFilePath);
            return Fail($"Could not write the file: {ex.Message}");
        }

        if (!result.Success)
            return new ApplyTranslationBatchResult(false, result.FailedIndex, result.Error);

        await _reloadService.ReloadLocalisationAsync(ct);

        // Read back rather than hashing composed text, because a binary format has no composed text
        // to hash. The guard reads the file the same way, so the two always agree.
        var written = await fs.File.ReadAllTextAsync(request.ProjectFilePath, ct);
        return new ApplyTranslationBatchResult(true,
            NewContentHash: LocalisationContentHash.Compute(written));
    }

    private static ApplyTranslationBatchResult Fail(string error)
    {
        return new ApplyTranslationBatchResult(false, null, error);
    }
}
