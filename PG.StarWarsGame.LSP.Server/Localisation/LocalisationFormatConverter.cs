// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.IO.Csv;
using PG.StarWarsGame.Localisation.IO.Properties;
using PG.StarWarsGame.Localisation.IO.Xml;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Localisation;

/// <summary>
///     Serialises a translation database to an exact path in a chosen text format.
/// </summary>
/// <remarks>
///     Deliberately not folded into <see cref="ILocalisationSeedFileWriter" />, which exists to
///     create a *new* project file and therefore owns the conventional file name
///     (<c>MasterTextFile.csv</c>). A conversion has to keep the name it started with - a credits
///     file renamed to <c>MasterTextFile</c> would stop being recognised as credits at all, since
///     detection is by filename - and it has to accept an ordered database, because credits repeat
///     their keys and <see cref="IKeyedTranslationDatabase" /> cannot hold them.
/// </remarks>
public interface ILocalisationFormatConverter
{
    /// <summary>The extension a format is written with, or null if it has no text writer.</summary>
    string? ExtensionFor(string format);

    /// <summary>
    ///     Writes <paramref name="db" /> to <paramref name="targetPath" />. Returns false if the
    ///     format has no writer. Does not check for an existing file - the caller owns that policy.
    /// </summary>
    Task<bool> WriteAsync(ITranslationDatabase db, string format, string targetPath, CancellationToken ct);
}

public sealed class LocalisationFormatConverter : ILocalisationFormatConverter
{
    private readonly ICsvTranslationExporter _csvExporter;
    private readonly IFileHelper _fileHelper;
    private readonly ILanguageService _langService;
    private readonly IPropertiesTranslationExporter _nlsExporter;
    private readonly IXmlTranslationExporter _xmlExporter;

    public LocalisationFormatConverter(
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

    public string? ExtensionFor(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "csv" => ".csv",
            "xml" => ".xml",
            "nls" => ".properties",
            _ => null
        };
    }

    public async Task<bool> WriteAsync(
        ITranslationDatabase db, string format, string targetPath, CancellationToken ct)
    {
        var content = format.ToLowerInvariant() switch
        {
            "csv" => _csvExporter.Export(db),
            "xml" => _xmlExporter.Export(db).ToString(),
            "nls" => _nlsExporter.Export(db, _langService.Default),
            _ => null
        };
        if (content is null) return false;

        var fs = _fileHelper.FileSystem;
        var directory = fs.Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory)) fs.Directory.CreateDirectory(directory);
        await fs.File.WriteAllTextAsync(targetPath, content, ct);
        return true;
    }
}
