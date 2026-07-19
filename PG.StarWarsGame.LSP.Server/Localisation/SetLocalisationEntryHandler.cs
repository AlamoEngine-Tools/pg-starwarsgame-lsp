// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class SetLocalisationEntryHandler
    : IJsonRpcRequestHandler<SetLocalisationEntryParams, LocalisationWriteResult>
{
    private readonly ILspConfigurationProvider _config;
    private readonly ILocalisationEntryWriter _entryWriter;
    private readonly IFileHelper _fileHelper;
    private readonly ILogger<SetLocalisationEntryHandler> _logger;
    private readonly IModProjectReloadService _reloadService;

    public SetLocalisationEntryHandler(
        ILocalisationEntryWriter entryWriter,
        IFileHelper fileHelper,
        IModProjectReloadService reloadService,
        ILogger<SetLocalisationEntryHandler> logger,
        ILspConfigurationProvider config)
    {
        _entryWriter = entryWriter;
        _fileHelper = fileHelper;
        _reloadService = reloadService;
        _logger = logger;
        _config = config;
    }

    public async Task<LocalisationWriteResult> Handle(SetLocalisationEntryParams request, CancellationToken ct)
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

        var written = await _entryWriter.UpsertAsync(
            request.ProjectFilePath, request.Key, request.Translations, ct);
        if (!written)
        {
            _logger.LogWarning("aet/setLocalisationEntry: could not write '{Key}' to '{Path}'.",
                request.Key, request.ProjectFilePath);
            return LocalisationWriteResult.Fail($"Could not write '{request.Key}' - unsupported file format.");
        }

        var newContent = await fs.File.ReadAllTextAsync(request.ProjectFilePath, ct);
        await _reloadService.ReloadLocalisationAsync(ct);

        return LocalisationWriteResult.Ok(LocalisationContentHash.Compute(newContent));
    }
}