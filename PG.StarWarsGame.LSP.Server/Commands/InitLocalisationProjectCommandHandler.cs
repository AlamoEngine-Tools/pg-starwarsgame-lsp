// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.Languages;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Commands;

public sealed class InitLocalisationProjectCommandHandler : ExecuteCommandHandlerBase
{
    public const string CommandName = "aet-eaw-edit.lsp.initLocalisationProject";

    private readonly IBaselineTranslationProvider _baselineProvider;
    private readonly ITranslationDatabaseFactory _factory;
    private readonly IFileHelper _fileHelper;
    private readonly IModProjectFileWriter _fileWriter;
    private readonly ILanguageService _langService;
    private readonly ILogger<InitLocalisationProjectCommandHandler> _logger;
    private readonly IModProjectReloadService _reloadService;
    private readonly ILocalisationSeedFileWriter _seedWriter;
    private readonly ILspConfigurationProvider _config;

    public InitLocalisationProjectCommandHandler(
        IBaselineTranslationProvider baselineProvider,
        ITranslationDatabaseFactory factory,
        ILanguageService langService,
        IFileHelper fileHelper,
        IModProjectReloadService reloadService,
        IModProjectFileWriter fileWriter,
        ILocalisationSeedFileWriter seedWriter,
        ILogger<InitLocalisationProjectCommandHandler> logger,
        ILspConfigurationProvider config)
    {
        _baselineProvider = baselineProvider;
        _factory = factory;
        _langService = langService;
        _fileHelper = fileHelper;
        _reloadService = reloadService;
        _fileWriter = fileWriter;
        _seedWriter = seedWriter;
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

        var fs = _fileHelper.FileSystem;
        var rootLayer = _reloadService.LastWorkspaceConfig?.Layers
            .OrderByDescending(l => l.Rank).FirstOrDefault();

        string format;
        string targetDir;
        string? bootstrapDirectory = null;

        if (rootLayer is { TextResourceType: { } rootType, TextRoots.Count: > 0 })
        {
            // The root .pgproj already declares a localisation node — it wins over any
            // client-supplied format/directory, which are ignored entirely.
            format = rootType;
            targetDir = rootLayer.TextRoots[0];
        }
        else
        {
            // Bootstrap case: no existing localisation config. Both format and directory must
            // come from the client (it prompts using the VS Code setting only as a last resort).
            var args = request.Arguments?.FirstOrDefault() as JObject;
            var clientFormat = args?.Value<string>("format");
            var clientDirectory = args?.Value<string>("directory");
            if (string.IsNullOrWhiteSpace(clientFormat) || string.IsNullOrWhiteSpace(clientDirectory))
            {
                _logger.LogWarning(
                    "aet-eaw-edit.lsp.initLocalisationProject: no existing localisation config and no " +
                    "format/directory provided to bootstrap one.");
                return Unit.Value;
            }

            if (rootLayer?.ProjectPath is not { } pgprojPath)
            {
                _logger.LogWarning(
                    "aet-eaw-edit.lsp.initLocalisationProject: no .pgproj found; cannot bootstrap a " +
                    "localisation project.");
                return Unit.Value;
            }

            format = clientFormat;
            bootstrapDirectory = clientDirectory;
            targetDir = fs.Path.Combine(fs.Path.GetDirectoryName(pgprojPath)!, clientDirectory);
        }

        var fileName = LocalisationFormatUtility.ToSeedFileName(format);
        if (fileName is null)
        {
            _logger.LogWarning("aet-eaw-edit.lsp.initLocalisationProject: unsupported format '{Format}'.", format);
            return Unit.Value;
        }

        var targetPath = fs.Path.Combine(targetDir, fileName);

        if (fs.File.Exists(targetPath))
        {
            _logger.LogWarning("aet-eaw-edit.lsp.initLocalisationProject: '{Path}' already exists; not overwriting.",
                targetPath);
            return Unit.Value;
        }

        var languages = _langService.OfficiallySupported();
        var eawDb = _baselineProvider.GetMasterText(GameContext.EaW, languages);
        var focDb = _baselineProvider.GetMasterText(GameContext.FoC, languages);

        var merged = _factory.CreateKeyed(languages);
        foreach (var entry in eawDb)
        foreach (var kv in entry.Translations)
            merged.SetTranslation(entry.Key, kv.Key, kv.Value);
        foreach (var entry in focDb)
        foreach (var kv in entry.Translations)
            merged.SetTranslation(entry.Key, kv.Key, kv.Value);

        var writtenPath = await _seedWriter.WriteAsync(merged, format, targetDir, ct);
        if (writtenPath is null)
        {
            _logger.LogWarning("aet-eaw-edit.lsp.initLocalisationProject: unsupported format '{Format}'.", format);
            return Unit.Value;
        }

        _logger.LogInformation("aet-eaw-edit.lsp.initLocalisationProject: created '{Path}'.", writtenPath);

        if (bootstrapDirectory is not null)
        {
            // Bootstrapped: persist the new localisation node so the project is self-describing
            // from now on, then fully re-resolve the .pgproj (a scoped reload wouldn't pick up
            // the new config since it reuses the stale WorkspaceConfiguration).
            await _fileWriter.SetLocalisationAsync(
                rootLayer!.ProjectPath!, format.ToUpperInvariant(), bootstrapDirectory, ct);
            await _reloadService.ReloadAsync(ct);
        }
        else
        {
            await _reloadService.ReloadLocalisationAsync(ct);
        }

        return Unit.Value;
    }

    protected override ExecuteCommandRegistrationOptions CreateRegistrationOptions(
        ExecuteCommandCapability capability, ClientCapabilities clientCapabilities)
    {
        return new ExecuteCommandRegistrationOptions { Commands = new Container<string>(CommandName) };
    }
}