// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class AddLocalisationLanguageHandler
    : IJsonRpcRequestHandler<AddLocalisationLanguageParams, LocalisationWriteResult>
{
    private readonly ILocalisationEntryWriter _entryWriter;
    private readonly IFileHelper _fileHelper;
    private readonly ILogger<AddLocalisationLanguageHandler> _logger;
    private readonly IModProjectReloadService _reloadService;

    public AddLocalisationLanguageHandler(
        ILocalisationEntryWriter entryWriter,
        IFileHelper fileHelper,
        IModProjectReloadService reloadService,
        ILogger<AddLocalisationLanguageHandler> logger)
    {
        _entryWriter = entryWriter;
        _fileHelper = fileHelper;
        _reloadService = reloadService;
        _logger = logger;
    }

    public async Task<LocalisationWriteResult> Handle(AddLocalisationLanguageParams request, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        if (string.IsNullOrWhiteSpace(request.ProjectFilePath) || !fs.File.Exists(request.ProjectFilePath))
            return LocalisationWriteResult.Fail($"File not found: {request.ProjectFilePath}");

        if (string.IsNullOrWhiteSpace(request.Language))
            return LocalisationWriteResult.Fail("A language is required.");

        var guardFailure = await LocalisationConcurrencyGuard.CheckAsync(
            fs, request.ProjectFilePath, request.ExpectedContentHash, ct);
        if (guardFailure is not null) return guardFailure;

        var added = await _entryWriter.AddLanguageAsync(request.ProjectFilePath, request.Language, ct);
        if (!added)
        {
            _logger.LogWarning(
                "aet/addLocalisationLanguage: could not add '{Language}' to '{Path}'.",
                request.Language, request.ProjectFilePath);
            return LocalisationWriteResult.Fail(
                $"Could not add language '{request.Language}' — it may already be present, or this file's " +
                "format doesn't support multiple languages.");
        }

        var newContent = await fs.File.ReadAllTextAsync(request.ProjectFilePath, ct);
        await _reloadService.ReloadLocalisationAsync(ct);

        return LocalisationWriteResult.Ok(LocalisationContentHash.Compute(newContent));
    }
}
