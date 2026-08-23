// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using System.Xml.Linq;
using CsvHelper;
using CsvHelper.Configuration;
using PG.StarWarsGame.Files.DAT.Services;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.IO.Properties;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <inheritdoc />
public sealed class LocalisationRowReader : ILocalisationRowReader
{
    private const string XmlNs = "urn:alamoenginetools:localisation:v1";

    private readonly IDatFileService _datFileService;
    private readonly ITranslationDatabaseFactory _factory;
    private readonly IFileHelper _fileHelper;
    private readonly ILanguageService _langService;
    private readonly IPropertiesTranslationImporter _nlsImporter;

    private readonly ILspConfigurationProvider _configProvider;

    public LocalisationRowReader(
        IPropertiesTranslationImporter nlsImporter,
        ITranslationDatabaseFactory factory,
        ILanguageService langService,
        IDatFileService datFileService,
        IFileHelper fileHelper,
        ILspConfigurationProvider configProvider)
    {
        _nlsImporter = nlsImporter;
        _factory = factory;
        _langService = langService;
        _datFileService = datFileService;
        _fileHelper = fileHelper;
        _configProvider = configProvider;
    }

    public LocDocument Read(string text, string extension, string? fileName = null)
    {
        return extension switch
        {
            ".csv" => ReadCsv(text),
            ".xml" => ReadXml(text),
            ".properties" => ReadProperties(text, fileName),
            // .dat is binary - it has no text form to hand in. Use ReadFile.
            _ => throw new NotSupportedException($"No row reader for '{extension}'.")
        };
    }

    public LocDocument ReadFile(string filePath)
    {
        var fs = _fileHelper.FileSystem;
        var extension = fs.Path.GetExtension(filePath).ToLowerInvariant();

        return extension == ".dat"
            ? ReadDat(filePath)
            // The name is passed on because a single-language format carries its language there and
            // nowhere else; for the multi-language formats it is simply unused.
            : Read(fs.File.ReadAllText(filePath), extension, filePath);
    }

    // ── DAT ──────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Reads a compiled DAT. Its language is not in the file - it is in the name
    ///     (<c>creditstext_english.dat</c>) - so the column is derived from there, falling back to
    ///     the default language rather than refusing to open a file named unconventionally.
    ///     <para>
    ///         Rows carry no verbatim source: a DAT has no source text to preserve, so a save
    ///         rewrites the whole file. That is safe here in a way it is not for CSV, because the
    ///         format has no comments, quoting or whitespace that a rewrite could disturb.
    ///     </para>
    /// </summary>
    private LocDocument ReadDat(string filePath)
    {
        var model = _datFileService.Load(filePath).Content;

        var language = LocalisationFileNameLanguageResolver.Resolve(
            filePath, _langService,
            LocalisationFileNameLanguageResolver.Configured(
                _langService, _configProvider.Current.Localisation),
            out _);

        var identifier = language.LanguageIdentifier;
        var rows = new List<LocRowDto>(model.Count);

        for (var i = 0; i < model.Count; i++)
            rows.Add(new LocRowDto(i, model[i].Key, [new LocValueDto(identifier, model[i].Value)]));

        return new LocDocument(rows, [identifier]);
    }

    /// <summary>The file's own line ending, so writing it back does not silently normalise it.</summary>
    private static string DetectLineEnding(string text)
    {
        return text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    // ── CSV ──────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Read through CsvHelper rather than by splitting lines. A quoted field may contain commas,
    ///     escaped quotes and newlines, so a record is not a line - and under index addressing,
    ///     mistaking one for the other silently misaligns every row that follows.
    /// </summary>
    private static LocDocument ReadCsv(string text)
    {
        var lineEnding = DetectLineEnding(text);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null
        };

        using var reader = new StringReader(text);
        using var csv = new CsvReader(reader, config);

        if (!csv.Read() || !csv.ReadHeader()) return new LocDocument([], [], string.Empty, lineEnding);

        var header = csv.HeaderRecord ?? [];
        // Column 0 is the key; the rest name languages. A blank header cell declares nothing.
        var languages = header.Skip(1).Where(h => !string.IsNullOrWhiteSpace(h)).ToList();
        var preamble = csv.Parser.RawRecord.TrimEnd('\r', '\n');

        var rows = new List<LocRowDto>();
        while (csv.Read())
        {
            var values = new List<LocValueDto>(languages.Count);
            for (var i = 0; i < languages.Count; i++)
                values.Add(new LocValueDto(languages[i], csv.GetField(i + 1) ?? string.Empty));

            rows.Add(new LocRowDto(
                rows.Count,
                csv.GetField(0) ?? string.Empty,
                values,
                csv.Parser.RawRecord.TrimEnd('\r', '\n')));
        }

        return new LocDocument(rows, languages, preamble, lineEnding);
    }

    // ── XML ──────────────────────────────────────────────────────────────────

    private static LocDocument ReadXml(string text)
    {
        var lineEnding = DetectLineEnding(text);
        var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        var ns = XNamespace.Get(XmlNs);

        var elements = document.Root?.Elements(ns + "Localisation").ToList() ?? [];

        // Declared union in first-seen order: a language present on one element but not another is
        // still a column, and must be an editable empty cell on the rows that lack it.
        var languages = elements
            .Elements(ns + "TranslationData")
            .Elements(ns + "Translation")
            .Select(e => e.Attribute("Language")?.Value)
            .Where(l => !string.IsNullOrEmpty(l))
            .Select(l => l!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rows = new List<LocRowDto>();
        foreach (var element in elements)
        {
            var byLanguage = element
                .Elements(ns + "TranslationData")
                .Elements(ns + "Translation")
                .Where(e => e.Attribute("Language")?.Value is { Length: > 0 })
                .GroupBy(e => e.Attribute("Language")!.Value, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);

            var values = languages
                .Select(l => new LocValueDto(l, byLanguage.GetValueOrDefault(l, string.Empty)))
                .ToList();

            rows.Add(new LocRowDto(
                rows.Count,
                element.Attribute("key")?.Value ?? string.Empty,
                values,
                element.ToString(SaveOptions.DisableFormatting)));
        }

        return new LocDocument(rows, languages, string.Empty, lineEnding);
    }

    // ── .properties ──────────────────────────────────────────────────────────

    /// <summary>
    ///     Values come from the library importer so escapes are unescaped exactly as the rest of the
    ///     server sees them; positions come from a line scan, because the importer cannot say where
    ///     an entry was. The two are zipped by order, which holds because the importer preserves it.
    /// </summary>
    private LocDocument ReadProperties(string text, string? fileName)
    {
        var lineEnding = DetectLineEnding(text);

        // A .properties file holds one language and cannot name it inside, so it comes from the file
        // name. Without a name to go on - Read called with text alone - the workspace's configured
        // game language is the only thing left to assume.
        var configured = LocalisationFileNameLanguageResolver.Configured(
            _langService, _configProvider.Current.Localisation);
        var language = fileName is null
            ? configured
            : LocalisationFileNameLanguageResolver.Resolve(fileName, _langService, configured, out _);

        var db = _factory.CreateOrdered([language]);
        using (var reader = new StringReader(text))
        {
            _nlsImporter.Import(reader, language, db);
        }

        var lines = text.Split('\n');
        var rows = new List<LocRowDto>();
        var leading = new StringBuilder();
        var preamble = string.Empty;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimStart();

            // Comments and blanks are the file's only documentation; they attach to the entry they
            // precede so they travel with it rather than being dropped on a rewrite.
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith('!'))
            {
                leading.Append(line).Append(lineEnding);
                continue;
            }

            if (rows.Count == 0 && preamble.Length == 0 && leading.Length > 0)
            {
                preamble = leading.ToString().TrimEnd('\r', '\n');
                leading.Clear();
            }

            var index = rows.Count;
            var value = index < db.Count && db[index].TryGetTranslation(language, out var v)
                ? v ?? string.Empty
                : string.Empty;

            rows.Add(new LocRowDto(
                index,
                index < db.Count ? db[index].Key : string.Empty,
                [new LocValueDto(language.LanguageIdentifier, value)],
                line,
                leading.ToString()));

            leading.Clear();
        }

        return new LocDocument(rows, [language.LanguageIdentifier], preamble, lineEnding);
    }
}
