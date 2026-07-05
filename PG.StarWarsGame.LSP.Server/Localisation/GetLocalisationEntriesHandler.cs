// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.IO.Csv;
using PG.StarWarsGame.Localisation.IO.Properties;
using PG.StarWarsGame.Localisation.IO.Xml;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Localisation;

// The webview's sole source of parsed data — replaces the client's own hand-rolled CSV/XML/NLS
// parsing (localisationEditorViewProvider.ts's parseCsv/parseXml/parseNls) with the same
// importers the rest of the server already uses, so the editor can never drift from what the
// LocalisationLoader/ExportLocalisationToDatHandler actually understand.
public sealed class GetLocalisationEntriesHandler
    : IJsonRpcRequestHandler<GetLocalisationEntriesParams, GetLocalisationEntriesResult>
{
    private const string XmlNs = "urn:alamoenginetools:localisation:v1";

    private readonly ICsvTranslationImporter _csvImporter;
    private readonly ITranslationDatabaseFactory _factory;
    private readonly IFileHelper _fileHelper;
    private readonly ILanguageService _langService;
    private readonly ILogger<GetLocalisationEntriesHandler> _logger;
    private readonly IPropertiesTranslationImporter _nlsImporter;
    private readonly IXmlTranslationImporter _xmlImporter;
    private readonly ILspConfigurationProvider _config;

    public GetLocalisationEntriesHandler(
        ICsvTranslationImporter csvImporter,
        IXmlTranslationImporter xmlImporter,
        IPropertiesTranslationImporter nlsImporter,
        ITranslationDatabaseFactory factory,
        ILanguageService langService,
        IFileHelper fileHelper,
        ILogger<GetLocalisationEntriesHandler> logger,
        ILspConfigurationProvider config)
    {
        _csvImporter = csvImporter;
        _xmlImporter = xmlImporter;
        _nlsImporter = nlsImporter;
        _factory = factory;
        _langService = langService;
        _fileHelper = fileHelper;
        _logger = logger;
        _config = config;
    }

    public async Task<GetLocalisationEntriesResult> Handle(
        GetLocalisationEntriesParams request, CancellationToken ct)
    {
        if (!_config.Current.Features.Tools.Localisation)
            return new GetLocalisationEntriesResult([], [], "", LocalisationFeatureDisabled.Message);

        var fs = _fileHelper.FileSystem;
        if (string.IsNullOrWhiteSpace(request.ProjectFilePath))
            return new GetLocalisationEntriesResult([], [], "", "No project file path provided.");

        if (!fs.File.Exists(request.ProjectFilePath))
            return new GetLocalisationEntriesResult([], [], "", $"File not found: {request.ProjectFilePath}");

        var content = await fs.File.ReadAllTextAsync(request.ProjectFilePath, ct);
        var ext = fs.Path.GetExtension(request.ProjectFilePath).ToLowerInvariant();

        var languages = _langService.OfficiallySupported();
        var db = _factory.CreateKeyed(languages);
        IReadOnlyList<string> declaredLanguages;

        try
        {
            switch (ext)
            {
                case ".csv":
                    using (var reader = new StringReader(content))
                    {
                        _csvImporter.Import(reader, db);
                    }

                    declaredLanguages = DiscoverCsvLanguages(content);
                    break;
                case ".xml":
                    var xdoc = XDocument.Parse(content);
                    _xmlImporter.Import(xdoc, db);
                    declaredLanguages = DiscoverXmlLanguages(xdoc);
                    break;
                case ".properties":
                    using (var reader = new StringReader(content))
                    {
                        _nlsImporter.Import(reader, _langService.Default, db);
                    }

                    declaredLanguages = [_langService.Default.LanguageIdentifier];
                    break;
                default:
                    return new GetLocalisationEntriesResult([], [], "", $"Unsupported format: {ext}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse localisation file '{Path}'.", request.ProjectFilePath);
            return new GetLocalisationEntriesResult([], [], "", $"Failed to parse file: {ex.Message}");
        }

        var entries = db
            .Select(e => new LocalisationEntryDto(
                e.Key, e.Translations.ToDictionary(kv => kv.Key.LanguageIdentifier, kv => kv.Value)))
            .ToList();

        return new GetLocalisationEntriesResult(entries, declaredLanguages, LocalisationContentHash.Compute(content));
    }

    // Languages come from the file's actual structure (header row / declared attributes), never
    // from "does any row have a non-empty value" — that heuristic is what silently dropped a
    // freshly-added, still-empty language column on the client before this chunk.
    private static IReadOnlyList<string> DiscoverCsvLanguages(string content)
    {
        var firstLine = content.Split('\n', 2)[0].TrimEnd('\r');
        return firstLine.Split(',').Skip(1).Where(c => c.Length > 0).ToList();
    }

    private static IReadOnlyList<string> DiscoverXmlLanguages(XDocument xdoc)
    {
        var ns = XNamespace.Get(XmlNs);
        return xdoc.Root?.Elements(ns + "Localisation")
            .Elements(ns + "TranslationData")
            .Elements(ns + "Translation")
            .Select(e => e.Attribute("Language")?.Value)
            .Where(l => !string.IsNullOrEmpty(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()! ?? [];
    }
}
