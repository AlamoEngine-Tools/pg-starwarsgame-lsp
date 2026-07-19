// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Commands;

public sealed class CreateLocalisationKeyCommandHandler : ExecuteCommandHandlerBase
{
    public const string CommandName = "aet-eaw-edit.lsp.createLocalisationKey";
    private readonly ILspConfigurationProvider _config;

    private readonly ILocalisationEntryWriter _entryWriter;
    private readonly IFileHelper _fileHelper;
    private readonly ILogger<CreateLocalisationKeyCommandHandler> _logger;
    private readonly IModProjectReloadService _reloadService;

    public CreateLocalisationKeyCommandHandler(
        ILocalisationEntryWriter entryWriter,
        IFileHelper fileHelper,
        IModProjectReloadService reloadService,
        ILogger<CreateLocalisationKeyCommandHandler> logger,
        ILspConfigurationProvider config)
    {
        _entryWriter = entryWriter;
        _fileHelper = fileHelper;
        _reloadService = reloadService;
        _logger = logger;
        _config = config;
    }

    public override async Task<Unit> Handle(ExecuteCommandParams request, CancellationToken ct)
    {
        if (!_config.Current.Features.Tools.Localisation)
        {
            _logger.LogWarning("{Cmd}: {Reason}", CommandName, LocalisationFeatureDisabled.Message);
            return Unit.Value;
        }

        if (request.Arguments is null || request.Arguments.Count < 2)
        {
            _logger.LogWarning("{Cmd}: missing arguments.", CommandName);
            return Unit.Value;
        }

        var keyName = request.Arguments[0]?.Value<string>();
        var filePath = request.Arguments[1]?.Value<string>();

        if (string.IsNullOrWhiteSpace(keyName))
        {
            _logger.LogWarning("{Cmd}: missing key name.", CommandName);
            return Unit.Value;
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.LogWarning("{Cmd}: missing file path.", CommandName);
            return Unit.Value;
        }

        var translations = request.Arguments.Count > 2 ? request.Arguments[2] as JObject : null;

        var fs = _fileHelper.FileSystem;
        if (!fs.File.Exists(filePath))
        {
            _logger.LogWarning("{Cmd}: file '{Path}' not found.", CommandName, filePath);
            return Unit.Value;
        }

        // "Create" must never silently overwrite a key someone else already added between the
        // diagnostic firing and the user applying the quick-fix.
        if (await _entryWriter.ExistsAsync(filePath, keyName, ct))
        {
            _logger.LogWarning("{Cmd}: key '{Key}' already exists in '{Path}'.", CommandName, keyName, filePath);
            return Unit.Value;
        }

        var translationDict = translations?.Properties()
            .ToDictionary(p => p.Name, p => p.Value.Value<string>() ?? string.Empty);
        var written = await _entryWriter.UpsertAsync(filePath, keyName, translationDict, ct);

        if (written)
            await _reloadService.ReloadAsync(ct);

        return Unit.Value;
    }

    protected override ExecuteCommandRegistrationOptions CreateRegistrationOptions(
        ExecuteCommandCapability capability, ClientCapabilities clientCapabilities)
    {
        return new ExecuteCommandRegistrationOptions { Commands = new Container<string>(CommandName) };
    }
}