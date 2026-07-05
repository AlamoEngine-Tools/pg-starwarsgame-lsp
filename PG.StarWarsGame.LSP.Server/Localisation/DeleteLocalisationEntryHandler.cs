// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class DeleteLocalisationEntryHandler
    : IJsonRpcRequestHandler<DeleteLocalisationEntryParams, LocalisationWriteResult>
{
    private readonly ILocalisationEntryWriter _entryWriter;
    private readonly IFileHelper _fileHelper;
    private readonly ILogger<DeleteLocalisationEntryHandler> _logger;
    private readonly IModProjectReloadService _reloadService;
    private readonly ILspConfigurationProvider _config;

    public DeleteLocalisationEntryHandler(
        ILocalisationEntryWriter entryWriter,
        IFileHelper fileHelper,
        IModProjectReloadService reloadService,
        ILogger<DeleteLocalisationEntryHandler> logger,
        ILspConfigurationProvider config)
    {
        _entryWriter = entryWriter;
        _fileHelper = fileHelper;
        _reloadService = reloadService;
        _logger = logger;
        _config = config;
    }

    public async Task<LocalisationWriteResult> Handle(DeleteLocalisationEntryParams request, CancellationToken ct)
    {
        if (!_config.Current.Features.Tools.Localisation)
            return LocalisationWriteResult.Fail(LocalisationFeatureDisabled.Message);

        var fs = _fileHelper.FileSystem;
        if (string.IsNullOrWhiteSpace(request.ProjectFilePath) || !fs.File.Exists(request.ProjectFilePath))
            return LocalisationWriteResult.Fail($"File not found: {request.ProjectFilePath}");

        if (string.IsNullOrWhiteSpace(request.Key))
            return LocalisationWriteResult.Fail("A key is required.");

        var guardFailure = await LocalisationConcurrencyGuard.CheckAsync(
            fs, request.ProjectFilePath, request.ExpectedContentHash, ct);
        if (guardFailure is not null) return guardFailure;

        var deleted = await _entryWriter.DeleteAsync(request.ProjectFilePath, request.Key, ct);
        if (!deleted)
        {
            _logger.LogWarning("aet/deleteLocalisationEntry: '{Key}' not found in '{Path}'.",
                request.Key, request.ProjectFilePath);
            return LocalisationWriteResult.Fail($"'{request.Key}' was not found in the file.");
        }

        var newContent = await fs.File.ReadAllTextAsync(request.ProjectFilePath, ct);
        await _reloadService.ReloadLocalisationAsync(ct);

        return LocalisationWriteResult.Ok(LocalisationContentHash.Compute(newContent));
    }
}
