// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.Files.DAT.Services;
using PG.StarWarsGame.Localisation.Baseline;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.IO.Csv;
using PG.StarWarsGame.Localisation.IO.Dat;
using PG.StarWarsGame.Localisation.IO.Properties;
using PG.StarWarsGame.Localisation.IO.Xml;
using PG.StarWarsGame.Localisation.Languages;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class ExportLocalisationToDatHandler
    : IJsonRpcRequestHandler<ExportLocalisationToDatParams, ExportLocalisationToDatResult>
{
    private readonly IBaselineTranslationProvider _baselineProvider;
    private readonly ICsvTranslationImporter _csvImporter;
    private readonly IDatTranslationExporter _datExporter;
    private readonly IDatFileService _datFileService;
    private readonly ITranslationDatabaseFactory _factory;
    private readonly IFileHelper _fileHelper;
    private readonly ILanguageService _langService;
    private readonly ILogger<ExportLocalisationToDatHandler> _logger;
    private readonly IPropertiesTranslationImporter _nlsImporter;
    private readonly IXmlTranslationImporter _xmlImporter;

    public ExportLocalisationToDatHandler(
        ICsvTranslationImporter csvImporter,
        IXmlTranslationImporter xmlImporter,
        IPropertiesTranslationImporter nlsImporter,
        IBaselineTranslationProvider baselineProvider,
        ITranslationDatabaseFactory factory,
        ILanguageService langService,
        IDatTranslationExporter datExporter,
        IDatFileService datFileService,
        IFileHelper fileHelper,
        ILogger<ExportLocalisationToDatHandler> logger)
    {
        _csvImporter = csvImporter;
        _xmlImporter = xmlImporter;
        _nlsImporter = nlsImporter;
        _baselineProvider = baselineProvider;
        _factory = factory;
        _langService = langService;
        _datExporter = datExporter;
        _datFileService = datFileService;
        _fileHelper = fileHelper;
        _logger = logger;
    }

    public Task<ExportLocalisationToDatResult> Handle(
        ExportLocalisationToDatParams request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectFilePath))
            return Task.FromResult(new ExportLocalisationToDatResult([], "No project file path provided."));

        var fs = _fileHelper.FileSystem;
        if (!fs.File.Exists(request.ProjectFilePath))
            return Task.FromResult(new ExportLocalisationToDatResult([], $"File not found: {request.ProjectFilePath}"));

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

        var ext = fs.Path.GetExtension(request.ProjectFilePath).ToLowerInvariant();
        try
        {
            using var fileStream = fs.File.OpenRead(request.ProjectFilePath);
            switch (ext)
            {
                case ".csv":
                    using (var reader = new StreamReader(fileStream))
                    {
                        _csvImporter.Import(reader, merged);
                    }

                    break;
                case ".xml":
                    using (var reader = new StreamReader(fileStream))
                    {
                        var xdoc = XDocument.Load(reader);
                        _xmlImporter.Import(xdoc, merged);
                    }

                    break;
                case ".properties":
                    using (var reader = new StreamReader(fileStream))
                    {
                        _nlsImporter.Import(reader, _langService.Default, merged);
                    }

                    break;
                default:
                    return Task.FromResult(new ExportLocalisationToDatResult([], $"Unsupported format: {ext}"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load workspace file '{Path}'.", request.ProjectFilePath);
            return Task.FromResult(new ExportLocalisationToDatResult([], $"Failed to load file: {ex.Message}"));
        }

        var dir = fs.Path.GetDirectoryName(request.ProjectFilePath)!;
        var written = new List<string>();
        foreach (var lang in languages)
        {
            var model = _datExporter.Export(merged, lang);
            if (model.Count == 0) continue;
            var outPath = fs.Path.Combine(dir, $"MasterTextFile_{lang.LanguageIdentifier}.dat");
            using var outStream = fs.File.Create(outPath);
            _datFileService.CreateDatFile(outStream, model, model.KeySortOrder);
            written.Add(outPath);
        }

        _logger.LogInformation("Exported {Count} DAT files from '{Path}'.", written.Count, request.ProjectFilePath);
        return Task.FromResult(new ExportLocalisationToDatResult(written, null));
    }
}