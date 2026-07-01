// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.IO.Csv;
using PG.StarWarsGame.Localisation.IO.Properties;
using PG.StarWarsGame.Localisation.IO.Xml;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class LocalisationSeedFileWriter : ILocalisationSeedFileWriter
{
    private readonly ICsvTranslationExporter _csvExporter;
    private readonly IFileHelper _fileHelper;
    private readonly ILanguageService _langService;
    private readonly IPropertiesTranslationExporter _nlsExporter;
    private readonly IXmlTranslationExporter _xmlExporter;

    public LocalisationSeedFileWriter(
        ICsvTranslationExporter csvExporter,
        IXmlTranslationExporter xmlExporter,
        IPropertiesTranslationExporter nlsExporter,
        ILanguageService langService,
        IFileHelper fileHelper)
    {
        _csvExporter = csvExporter;
        _xmlExporter = xmlExporter;
        _nlsExporter = nlsExporter;
        _langService = langService;
        _fileHelper = fileHelper;
    }

    public async Task<string?> WriteAsync(
        IKeyedTranslationDatabase db, string format, string targetDir, CancellationToken ct)
    {
        var fileName = LocalisationFormatUtility.ToSeedFileName(format);
        if (fileName is null) return null;

        var content = format.ToLowerInvariant() switch
        {
            "csv" => _csvExporter.Export(db),
            "xml" => _xmlExporter.Export(db).ToString(),
            "nls" => _nlsExporter.Export(db, _langService.Default),
            _ => null
        };
        if (content is null) return null;

        var fs = _fileHelper.FileSystem;
        var targetPath = fs.Path.Combine(targetDir, fileName);
        fs.Directory.CreateDirectory(targetDir);
        await fs.File.WriteAllTextAsync(targetPath, content, ct);
        return targetPath;
    }
}
