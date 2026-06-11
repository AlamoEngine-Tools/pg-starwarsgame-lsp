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
using PG.StarWarsGame.Localisation.IO.Csv;
using PG.StarWarsGame.Localisation.IO.Properties;
using PG.StarWarsGame.Localisation.IO.Xml;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Commands;

public sealed class InitLocalisationProjectCommandHandler : ExecuteCommandHandlerBase
{
    public const string CommandName = "aet-eaw-edit.lsp.initLocalisationProject";

    private readonly IBaselineTranslationProvider _baselineProvider;
    private readonly ICsvTranslationExporter _csvExporter;
    private readonly IXmlTranslationExporter _xmlExporter;
    private readonly IPropertiesTranslationExporter _nlsExporter;
    private readonly ITranslationDatabaseFactory _factory;
    private readonly ILanguageService _langService;
    private readonly ILspConfigurationProvider _configProvider;
    private readonly IFileHelper _fileHelper;
    private readonly IModProjectReloadService _reloadService;
    private readonly ILogger<InitLocalisationProjectCommandHandler> _logger;

    public InitLocalisationProjectCommandHandler(
        IBaselineTranslationProvider baselineProvider,
        ICsvTranslationExporter csvExporter,
        IXmlTranslationExporter xmlExporter,
        IPropertiesTranslationExporter nlsExporter,
        ITranslationDatabaseFactory factory,
        ILanguageService langService,
        ILspConfigurationProvider configProvider,
        IFileHelper fileHelper,
        IModProjectReloadService reloadService,
        ILogger<InitLocalisationProjectCommandHandler> logger)
    {
        _baselineProvider = baselineProvider;
        _csvExporter = csvExporter;
        _xmlExporter = xmlExporter;
        _nlsExporter = nlsExporter;
        _factory = factory;
        _langService = langService;
        _configProvider = configProvider;
        _fileHelper = fileHelper;
        _reloadService = reloadService;
        _logger = logger;
    }

    public override async Task<Unit> Handle(ExecuteCommandParams request, CancellationToken ct)
    {
        if (request.Arguments?.FirstOrDefault() is not JObject args)
        {
            _logger.LogWarning("aet-eaw-edit.lsp.initLocalisationProject invoked without arguments.");
            return Unit.Value;
        }

        var format = args.Value<string>("format");
        if (string.IsNullOrWhiteSpace(format))
        {
            _logger.LogWarning("aet-eaw-edit.lsp.initLocalisationProject: missing format argument.");
            return Unit.Value;
        }

        var fileName = FormatToFilename(format);
        if (fileName is null)
        {
            _logger.LogWarning("aet-eaw-edit.lsp.initLocalisationProject: unsupported format '{Format}'.", format);
            return Unit.Value;
        }

        var fs = _fileHelper.FileSystem;
        var textRoots = _reloadService.LastWorkspaceConfig?.TextRoots ?? [];
        string targetDir;
        if (textRoots.Count == 1)
        {
            targetDir = textRoots[0];
        }
        else if (textRoots.Count > 1)
        {
            var workspaceRoot = _reloadService.LastWorkspaceRoots?.FirstOrDefault();
            if (workspaceRoot is null)
            {
                _logger.LogWarning("aet-eaw-edit.lsp.initLocalisationProject: multiple text roots but no workspace root available.");
                return Unit.Value;
            }
            targetDir = fs.Path.Combine(workspaceRoot, "Data", "Text");
        }
        else
        {
            var config = _configProvider.Current;
            if (config.ModPaths.Count == 0)
            {
                _logger.LogWarning("aet-eaw-edit.lsp.initLocalisationProject: no text roots or mod paths configured.");
                return Unit.Value;
            }
            targetDir = fs.Path.Combine(config.ModPaths[0], "Data", "Text");
        }

        var targetPath = fs.Path.Combine(targetDir, fileName);

        if (fs.File.Exists(targetPath))
        {
            _logger.LogWarning("aet-eaw-edit.lsp.initLocalisationProject: '{Path}' already exists; not overwriting.", targetPath);
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

        var content = format.ToLowerInvariant() switch
        {
            "csv" => _csvExporter.Export(merged),
            "xml" => _xmlExporter.Export(merged).ToString(),
            "nls" => _nlsExporter.Export(merged, _langService.Default),
            _ => null
        };

        if (content is null)
        {
            _logger.LogWarning("aet-eaw-edit.lsp.initLocalisationProject: unsupported format '{Format}'.", format);
            return Unit.Value;
        }

        fs.Directory.CreateDirectory(targetDir);
        await fs.File.WriteAllTextAsync(targetPath, content, ct);
        _logger.LogInformation("aet-eaw-edit.lsp.initLocalisationProject: created '{Path}'.", targetPath);

        await _reloadService.ReloadAsync(ct);

        return Unit.Value;
    }

    private static string? FormatToFilename(string format) => format.ToLowerInvariant() switch
    {
        "csv" => "MasterTextFile.csv",
        "xml" => "MasterTextFile.xml",
        "nls" => "MasterTextFile.properties",
        _ => null
    };

    protected override ExecuteCommandRegistrationOptions CreateRegistrationOptions(
        ExecuteCommandCapability capability, ClientCapabilities clientCapabilities)
    {
        return new ExecuteCommandRegistrationOptions { Commands = new Container<string>(CommandName) };
    }
}
