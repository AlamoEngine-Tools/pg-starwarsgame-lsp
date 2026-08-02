// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Commits a staged batch of position-addressed credits edits.
///     <para>
///         Writes to disk rather than sending <c>workspace/applyEdit</c>: the whole localisation
///         read path is disk-based, so an applyEdit would leave the file unsaved and make every
///         later read stale.
///     </para>
/// </summary>
public sealed class ApplyCreditsBatchHandler
    : IJsonRpcRequestHandler<ApplyCreditsBatchParams, ApplyCreditsBatchResult>
{
    private readonly ILspConfigurationProvider _config;
    private readonly ILocalisationDocumentEditor _editor;
    private readonly IFileHelper _fileHelper;
    private readonly ILogger<ApplyCreditsBatchHandler> _logger;
    private readonly IModProjectReloadService _reloadService;
    private readonly ILocalisationWriteLedger _writeLedger;

    public ApplyCreditsBatchHandler(
        ILocalisationDocumentEditor editor,
        IModProjectReloadService reloadService,
        IFileHelper fileHelper,
        ILogger<ApplyCreditsBatchHandler> logger,
        ILspConfigurationProvider config,
        ILocalisationWriteLedger writeLedger)
    {
        _writeLedger = writeLedger;
        _editor = editor;
        _reloadService = reloadService;
        _fileHelper = fileHelper;
        _logger = logger;
        _config = config;
    }

    public async Task<ApplyCreditsBatchResult> Handle(
        ApplyCreditsBatchParams request, CancellationToken ct)
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
            return new ApplyCreditsBatchResult(true,
                NewContentHash: LocalisationContentHash.Compute(unchanged));
        }

        LocalisationEditResult result;
        try
        {
            // The editor owns the write: .dat is binary, so there is no text for this handler to
            // save, and the DAT services write through a real file stream.
            result = await _editor.ApplyToFileAsync(request.ProjectFilePath, request.Commands, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write credits file '{Path}'.", request.ProjectFilePath);
            return Fail($"Could not write the file: {ex.Message}");
        }

        if (!result.Success)
            return new ApplyCreditsBatchResult(false, result.FailedIndex, result.Error);

        var written = await fs.File.ReadAllTextAsync(request.ProjectFilePath, ct);
        var writtenHash = LocalisationContentHash.Compute(written);

        // Recorded before the reload, so the watcher event for this write - which can arrive at any
        // point after it - is recognised as our own and does not reload everything a second time.
        _writeLedger.Record(request.ProjectFilePath, writtenHash);

        await _reloadService.ReloadLocalisationAsync(ct);

        return new ApplyCreditsBatchResult(true, NewContentHash: writtenHash);
    }

    private static ApplyCreditsBatchResult Fail(string error)
    {
        return new ApplyCreditsBatchResult(false, null, error);
    }
}
