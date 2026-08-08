// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CsvHelper;
using CsvHelper.Configuration;
using PG.Commons.Hashing;
using PG.StarWarsGame.Files.DAT.Data;
using PG.StarWarsGame.Files.DAT.Files;
using PG.StarWarsGame.Files.DAT.Services;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <inheritdoc />
public sealed class LocalisationDocumentEditor : ILocalisationDocumentEditor
{
    private const string XmlNs = "urn:alamoenginetools:localisation:v1";

    private readonly IDatFileService _datFileService;
    private readonly IFileHelper _fileHelper;
    private readonly ICrc32HashingService _hashing;
    private readonly ILocalisationRowReader _reader;

    public LocalisationDocumentEditor(
        ILocalisationRowReader reader,
        IDatFileService datFileService,
        ICrc32HashingService hashing,
        IFileHelper fileHelper)
    {
        _reader = reader;
        _datFileService = datFileService;
        _hashing = hashing;
        _fileHelper = fileHelper;
    }

    /// <summary>
    ///     Whether a file in this format can hold more than one language, and so can be given
    ///     another language column.
    /// </summary>
    /// <remarks>
    ///     The two that cannot hold a second language are <c>.properties</c> and <c>.dat</c>: both
    ///     store one language per file and carry it in the file name, the way Java's
    ///     <c>ResourceBundle</c> and the engine's own <c>mastertextfile_english.dat</c> do. Another
    ///     language means another file, not another column.
    ///     <para>
    ///         Both <c>addLanguage</c> refusals below defer to this, so does the read endpoint that
    ///         tells the client whether to offer the action, and so does
    ///         <see cref="LocalisationFileNameLanguageResolver.CarriesLanguageInFileName" /> - the list
    ///         of single-language formats lives here only.
    ///     </para>
    /// </remarks>
    public static bool SupportsMultipleLanguages(string extension)
    {
        return extension.ToLowerInvariant() is not (".properties" or ".dat");
    }

    public async Task<LocalisationEditResult> ApplyToFileAsync(
        string filePath, IReadOnlyList<LocEditCommandDto> commands, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var extension = fs.Path.GetExtension(filePath).ToLowerInvariant();

        if (extension != ".dat")
        {
            var original = await fs.File.ReadAllTextAsync(filePath, ct);
            var composed = Apply(original, extension, commands, filePath);
            if (!composed.Success) return composed;

            await fs.File.WriteAllTextAsync(filePath, composed.NewText!, ct);
            return composed;
        }

        return ApplyDat(filePath, commands);
    }

    /// <inheritdoc />
    public LocalisationEditResult DryRunFile(
        string filePath, IReadOnlyList<LocEditCommandDto> commands)
    {
        var fs = _fileHelper.FileSystem;
        var extension = fs.Path.GetExtension(filePath).ToLowerInvariant();

        // A DAT has no text to compose, so it is exercised the only way it can be: apply the
        // commands to its rows in memory and report whether they all landed. Nothing is written.
        if (extension == ".dat")
        {
            LocDocument document;
            try
            {
                document = _reader.ReadFile(filePath);
            }
            catch (Exception ex)
            {
                return LocalisationEditResult.Fail(0, $"Failed to read the file: {ex.Message}");
            }

            var rows = document.Rows.ToList();
            for (var i = 0; i < commands.Count; i++)
                if (ApplyToRows(rows, commands[i], document.Languages) is { } error)
                    return LocalisationEditResult.Fail(i, error);

            return LocalisationEditResult.Ok(string.Empty);
        }

        return Apply(fs.File.ReadAllText(filePath), extension, commands, filePath);
    }

    /// <inheritdoc />
    public (IReadOnlyList<LocRowDto> Rows, IReadOnlyList<string> Languages, string? Error) DryRunRows(
        string filePath, IReadOnlyList<LocEditCommandDto> commands)
    {
        var fs = _fileHelper.FileSystem;
        var extension = fs.Path.GetExtension(filePath).ToLowerInvariant();

        LocDocument document;
        try
        {
            document = _reader.ReadFile(filePath);
        }
        catch (Exception ex)
        {
            return ([], [], $"Failed to read the file: {ex.Message}");
        }

        // Applied to the rows in memory rather than by composing and re-reading: a .dat has no text
        // to compose, and for the others a second parse would buy nothing here.
        var rows = document.Rows.ToList();
        var languages = document.Languages.ToList();

        for (var i = 0; i < commands.Count; i++)
        {
            var command = commands[i];

            // addLanguage changes the language list, not a row - ApplyToRows does not own that list
            // and refuses the command outright. Handled here instead: the new language is added and
            // every row is left without a value for it, which is exactly the state the coverage
            // check should see (a column added and not yet filled in).
            if (string.Equals(command.Kind, "addLanguage", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(command.Language)
                    && !languages.Contains(command.Language, StringComparer.OrdinalIgnoreCase))
                    languages.Add(command.Language);

                continue;
            }

            if (ApplyToRows(rows, command, languages) is { } error)
                return ([], [], $"Change {i + 1}: {error}");
        }

        return (rows, languages, null);
    }

    /// <inheritdoc />
    public async Task<LocalisationEditResult> ApplyKeyedToFileAsync(
        string filePath, IReadOnlyList<LocKeyedCommandDto> commands, CancellationToken ct)
    {
        var translated = TranslateKeyed(filePath, commands);
        if (!translated.Success)
            return LocalisationEditResult.Fail(translated.FailedIndex ?? 0, translated.Error!);

        return await ApplyToFileAsync(filePath, translated.Commands!, ct);
    }

    /// <summary>
    ///     Resolves key-addressed commands against the file as it currently stands.
    ///     <para>
    ///         This reads the document, and <see cref="ApplyToFileAsync" /> then reads it again. The
    ///         second read is accepted rather than threaded through: the XML path composes through
    ///         <c>XDocument</c> and never builds a <see cref="LocDocument" /> at all, so there is no
    ///         single document to share across all three formats, and plumbing one in would mean
    ///         reworking the round-trip code that must not be disturbed. A save is user-initiated
    ///         and rare; if it ever shows up in a profile, measure before restructuring.
    ///     </para>
    /// </summary>
    public KeyedTranslationResult TranslateKeyed(
        string filePath, IReadOnlyList<LocKeyedCommandDto> commands)
    {
        LocDocument document;
        try
        {
            document = _reader.ReadFile(filePath);
        }
        catch (NotSupportedException)
        {
            var extension = _fileHelper.FileSystem.Path.GetExtension(filePath).ToLowerInvariant();
            return KeyedTranslationResult.Fail(0, $"Unsupported format: {extension}");
        }

        return KeyedCommandTranslator.Translate(document, commands, _hashing);
    }

    /// <summary>
    ///     Applies a batch to a compiled DAT by rebuilding it from its rows.
    ///     <para>
    ///         There is no verbatim source to preserve here and nothing a rewrite could disturb -
    ///         the format has no comments, quoting or whitespace - so the whole file is written
    ///         back. The file's own sort order is preserved: rewriting an unsorted credits DAT as a
    ///         CRC-sorted one would scramble the crawl into checksum order.
    ///     </para>
    /// </summary>
    private LocalisationEditResult ApplyDat(
        string filePath, IReadOnlyList<LocEditCommandDto> commands)
    {
        var fs = _fileHelper.FileSystem;
        var sortOrder = _datFileService.Load(filePath).Content.KeySortOrder;
        var document = _reader.ReadFile(filePath);

        var rows = document.Rows.ToList();
        for (var i = 0; i < commands.Count; i++)
            if (ApplyToRows(rows, commands[i], document.Languages) is { } error)
                return LocalisationEditResult.Fail(i, error);

        var language = document.Languages.FirstOrDefault() ?? string.Empty;

        // Checked before anything is opened. A .dat stores keys as ASCII bytes and DatStringEntry
        // refuses anything else - but it did so from inside the write, as "Value contains non-ASCII
        // characters (Parameter 'value')", which names the wrong field and no row at all.
        for (var i = 0; i < rows.Count; i++)
            if (!rows[i].Key.All(char.IsAscii))
                return LocalisationEditResult.Fail(
                    i,
                    $"Row {i + 1}: '{rows[i].Key}' cannot be used as a key - a .dat stores keys as "
                    + "ASCII. Rename the entry using unaccented letters; the translation itself is "
                    + "unaffected.");

        // Materialised HERE, not left lazy. Building an entry is what validates its key, and the
        // sequence used to be enumerated inside CreateDatFile - by which point File.Create had
        // already truncated the file. One unwritable key destroyed the file it refused to write.
        var entries = rows.Select(r => new DatStringEntry(
            r.Key,
            _hashing.GetCrc32(r.Key, Encoding.ASCII),
            r.Values.FirstOrDefault(v => v.Language == language)?.Value ?? string.Empty)).ToList();

        var fileType = sortOrder == DatFileType.OrderedByCrc32
            ? DatFileType.OrderedByCrc32
            : DatFileType.NotOrdered;

        // Written beside the target and moved over it, so the original survives every way this can
        // fail - a rejected entry, a full disk, a crash halfway through. Truncating the real file
        // first and hoping the write succeeds is what turned a refused save into an empty file.
        var temporary = filePath + ".aettmp";
        try
        {
            using (var stream = fs.File.Create(temporary))
            {
                _datFileService.CreateDatFile(stream, entries, fileType);
            }

            fs.File.Move(temporary, filePath, true);
        }
        catch
        {
            // The original is still untouched; clear the partial away so it is not mistaken for a
            // localisation file by the next scan.
            if (fs.File.Exists(temporary)) fs.File.Delete(temporary);
            throw;
        }

        return LocalisationEditResult.Ok(string.Empty);
    }

    /// <summary>
    ///     The row-list half of the commands, shared by the DAT path. Text formats go through
    ///     <see cref="EditState" /> instead, which additionally tracks each row's original text.
    /// </summary>
    private static string? ApplyToRows(
        List<LocRowDto> rows, LocEditCommandDto command, IReadOnlyList<string> languages)
    {
        var index = command.Index ?? -1;
        var addressed = command.Kind is "setCell" or "setKey" or "deleteRow" or "moveRow";

        if (addressed && (index < 0 || index >= rows.Count))
            return $"Row {command.Index} is out of range (the file has {rows.Count} rows).";

        if (addressed && command.ExpectedKey is { } expected
            && !string.Equals(rows[index].Key, expected, StringComparison.Ordinal))
            return $"Row {index} is no longer '{expected}'. Reload before editing again.";

        switch (command.Kind)
        {
            case "setCell":
                var language = command.Language ?? languages.FirstOrDefault() ?? string.Empty;
                rows[index] = rows[index] with
                {
                    Values = [new LocValueDto(language, command.Value ?? string.Empty)]
                };
                return null;

            case "setKey":
                rows[index] = rows[index] with { Key = command.Key ?? string.Empty };
                return null;

            case "deleteRow":
                rows.RemoveAt(index);
                break;

            case "moveRow":
                var to = command.ToIndex ?? 0;
                if (to < 0 || to >= rows.Count)
                    return $"Cannot move to row {to} (the file has {rows.Count} rows).";
                var moved = rows[index];
                rows.RemoveAt(index);
                rows.Insert(to, moved);
                break;

            case "insertRow":
                var at = command.Index ?? rows.Count;
                if (at < 0 || at > rows.Count)
                    return $"Cannot insert at row {at} (the file has {rows.Count} rows).";
                rows.Insert(at, new LocRowDto(at, command.Key ?? string.Empty, [.. command.Values ?? []]));
                break;

            // A DAT holds exactly one language - the one in its filename - so a second column
            // cannot be represented.
            case "addLanguage":
                return "A .dat file holds a single language and cannot take another column.";

            default:
                return $"Unknown command '{command.Kind}'.";
        }

        // Positions are the addressing scheme, so they have to stay equal to the index.
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Index != i)
                rows[i] = rows[i] with { Index = i };

        return null;
    }

    public LocalisationEditResult Apply(
        string originalText, string extension, IReadOnlyList<LocEditCommandDto> commands,
        string? fileName = null)
    {
        // XML is composed through XDocument rather than through row slices: with preserved
        // whitespace it already re-serialises untouched markup unchanged, and rebuilding elements
        // from slices would mean reimplementing namespace and escaping rules.
        if (extension == ".xml") return ApplyXml(originalText, commands);

        LocDocument document;
        try
        {
            document = _reader.Read(originalText, extension, fileName);
        }
        catch (NotSupportedException)
        {
            return LocalisationEditResult.Fail(0, $"Unsupported format: {extension}");
        }

        var state = new EditState(document, extension);

        for (var i = 0; i < commands.Count; i++)
            if (state.Apply(commands[i]) is { } error)
                return LocalisationEditResult.Fail(i, error);

        return LocalisationEditResult.Ok(state.Compose());
    }

    // ── XML ──────────────────────────────────────────────────────────────────

    private static LocalisationEditResult ApplyXml(string text, IReadOnlyList<LocEditCommandDto> commands)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            return LocalisationEditResult.Fail(0, $"Failed to parse file: {ex.Message}");
        }

        var ns = XNamespace.Get(XmlNs);
        var rows = document.Root?.Elements(ns + "Localisation").ToList() ?? [];

        for (var i = 0; i < commands.Count; i++)
            if (ApplyXmlCommand(commands[i], rows, ns) is { } error)
                return LocalisationEditResult.Fail(i, error);

        var lineEnding = DetectLineEnding(text);
        var declaration = document.Declaration is { } d ? d + lineEnding : string.Empty;
        var body = Serialise(document.Root);

        // An XML parser is required to normalise every line ending to LF (XML 1.0 section 2.11), so a
        // CRLF file comes out of the tree in LF no matter how carefully the writer is configured -
        // and a one-cell edit rewrote every line of it. The declaration's ending was already restored
        // here; the body's never was. Safe as a blind replace because the parse guarantees no CRLF
        // survived to be doubled.
        if (lineEnding != "\n") body = body.Replace("\n", lineEnding, StringComparison.Ordinal);

        return LocalisationEditResult.Ok(declaration + body);
    }

    /// <summary>
    ///     Writes the tree back without touching its whitespace. <c>XNode.ToString</c> cannot be
    ///     used: it normalises every line ending to CRLF, which would rewrite every line of an
    ///     LF-terminated file on a one-cell edit.
    /// </summary>
    private static string Serialise(XElement? root)
    {
        if (root is null) return string.Empty;

        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            NewLineHandling = NewLineHandling.None,
            Indent = false
        };

        using var writer = new StringWriter();
        using (var xml = XmlWriter.Create(writer, settings))
        {
            root.WriteTo(xml);
        }

        return writer.ToString();
    }

    /// <summary>
    ///     Adds a translation element after the ones already there, indented to match.
    /// </summary>
    /// <remarks>
    ///     The document is parsed and written with whitespace preserved, so a new element carries
    ///     none of its own: without copying the indentation in front of a sibling it would land
    ///     inline, and adding one language would show up in a diff as the whole row reformatted.
    /// </remarks>
    private static void AppendTranslation(XElement data, XElement translation)
    {
        var last = data.Elements().LastOrDefault();
        if (last is null)
        {
            data.Add(translation);
            return;
        }

        if (last.PreviousNode is XText indent && string.IsNullOrWhiteSpace(indent.Value))
            last.AddAfterSelf(new XText(indent.Value), translation);
        else
            last.AddAfterSelf(translation);
    }

    private static string? ApplyXmlCommand(LocEditCommandDto command, List<XElement> rows, XNamespace ns)
    {
        if (command.Kind == "addLanguage")
            return string.IsNullOrWhiteSpace(command.Language)
                ? "No language given."
                : null; // A language exists in XML only where a value does; nothing to declare.

        if (command.Index is not { } index || index < 0 || index >= rows.Count)
            return $"Row {command.Index} is out of range (the file has {rows.Count} rows).";

        var element = rows[index];
        if (command.ExpectedKey is { } expected
            && !string.Equals(element.Attribute("key")?.Value, expected, StringComparison.Ordinal))
            return $"Row {index} is no longer '{expected}'. Reload before editing again.";

        switch (command.Kind)
        {
            case "setCell":
                if (string.IsNullOrWhiteSpace(command.Language)) return "No language given.";

                var data = element.Elements(ns + "TranslationData").FirstOrDefault();
                if (data is null)
                {
                    data = new XElement(ns + "TranslationData");
                    element.Add(data);
                }

                var translation = data
                    .Elements(ns + "Translation")
                    .FirstOrDefault(e => string.Equals(
                        e.Attribute("Language")?.Value, command.Language, StringComparison.OrdinalIgnoreCase));

                // A language exists in this format only where a value does - there is no column list
                // to extend, which is why addLanguage has nothing to do here. So writing a value for
                // a language the row does not carry yet is how that language comes into existence.
                // Refusing instead meant every write to a newly added language failed on save.
                if (translation is null)
                {
                    translation = new XElement(ns + "Translation",
                        new XAttribute("Language", command.Language));
                    AppendTranslation(data, translation);
                }

                translation.Value = command.Value ?? string.Empty;
                return null;

            case "setKey":
                element.SetAttributeValue("key", command.Key ?? string.Empty);
                return null;

            case "deleteRow":
                element.Remove();
                rows.RemoveAt(index);
                return null;

            default:
                return $"Unsupported command '{command.Kind}' for XML.";
        }
    }

    private static string DetectLineEnding(string text)
    {
        return text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    // ── line-oriented formats ────────────────────────────────────────────────

    /// <summary>
    ///     One row mid-edit. <see cref="Source" /> is the verbatim original and is used verbatim
    ///     until something actually changes the row, which is what keeps an untouched row's original
    ///     quoting and spacing intact.
    /// </summary>
    private sealed class RowState
    {
        public required string Key { get; set; }
        public required List<LocValueDto> Values { get; init; }
        public required string Source { get; set; }
        public required string Leading { get; init; }
        public bool Modified { get; set; }
    }

    private sealed class EditState
    {
        private readonly string _extension;
        private readonly string _lineEnding;
        private readonly List<string> _languages;
        private readonly List<RowState> _rows;
        private string _preamble;

        public EditState(LocDocument document, string extension)
        {
            _extension = extension;
            _lineEnding = document.LineEnding;
            _preamble = document.Preamble;
            _languages = [.. document.Languages];
            _rows = document.Rows
                .Select(r => new RowState
                {
                    Key = r.Key,
                    Values = [.. r.Values],
                    Source = r.Source,
                    Leading = r.Leading
                })
                .ToList();
        }

        /// <summary>Applies one command, returning null on success or the user-facing error.</summary>
        public string? Apply(LocEditCommandDto command)
        {
            switch (command.Kind)
            {
                case "addLanguage": return AddLanguage(command);
                case "insertRow": return InsertRow(command);
                case "setCell": return Mutate(command, SetCell);
                case "setKey": return Mutate(command, SetKey);
                case "deleteRow": return DeleteRow(command);
                case "moveRow": return MoveRow(command);
                default: return $"Unknown command '{command.Kind}'.";
            }
        }

        public string Compose()
        {
            var builder = new StringBuilder();

            // A CSV that had nothing in it has no header line to preserve, and one has to be there
            // or the first row written becomes the header when the file is read back - a line
            // silently swallowed and every value shifted a column. The languages come from the rows
            // that arrived; see InsertRow, which is what establishes them.
            var preamble = _preamble.Length == 0 && _extension == ".csv" && _languages.Count > 0
                ? CsvHeader()
                : _preamble;

            if (preamble.Length > 0) builder.Append(preamble).Append(_lineEnding);

            foreach (var row in _rows)
            {
                builder.Append(row.Leading);
                builder.Append(row.Modified ? Serialise(row) : row.Source);
                builder.Append(_lineEnding);
            }

            return builder.ToString();
        }

        /// <summary>
        ///     The header line for a CSV that never had one: the key column, then one per language.
        ///     Written through CsvHelper like every row, so a language whose name needed quoting is
        ///     quoted the same way the reader expects.
        /// </summary>
        private string CsvHeader()
        {
            using var writer = new StringWriter();
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

            // Column 0 is the key; the rest name languages - see LocalisationRowReader.ReadCsv.
            csv.WriteField("key");
            foreach (var language in _languages) csv.WriteField(language);

            csv.NextRecord();
            return writer.ToString().TrimEnd('\r', '\n');
        }

        private string Serialise(RowState row)
        {
            if (_extension == ".properties")
                return $"{row.Key}={row.Values.FirstOrDefault()?.Value ?? string.Empty}";

            // Through CsvHelper so an edited value that now contains a comma, quote or newline is
            // quoted the same way the importers expect to read it back.
            using var writer = new StringWriter();
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture));

            csv.WriteField(row.Key);
            foreach (var language in _languages)
                csv.WriteField(row.Values
                    .FirstOrDefault(v => string.Equals(v.Language, language, StringComparison.OrdinalIgnoreCase))
                    ?.Value ?? string.Empty);

            csv.NextRecord();
            return writer.ToString().TrimEnd('\r', '\n');
        }

        // ── commands ─────────────────────────────────────────────────────────

        private string? Mutate(LocEditCommandDto command, Func<RowState, LocEditCommandDto, string?> mutate)
        {
            if (Resolve(command, out var row, out var error) is false) return error;

            var result = mutate(row!, command);
            if (result is not null) return result;

            row!.Modified = true;
            return null;
        }

        private string? SetCell(RowState row, LocEditCommandDto command)
        {
            var index = _languages.FindIndex(l =>
                string.Equals(l, command.Language, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return $"'{command.Language}' is not a language in this file.";

            var language = _languages[index];
            var existing = row.Values.FindIndex(v =>
                string.Equals(v.Language, language, StringComparison.OrdinalIgnoreCase));
            var value = new LocValueDto(language, command.Value ?? string.Empty);

            if (existing >= 0) row.Values[existing] = value;
            else row.Values.Add(value);

            return null;
        }

        private static string? SetKey(RowState row, LocEditCommandDto command)
        {
            row.Key = command.Key ?? string.Empty;
            return null;
        }

        private string? InsertRow(LocEditCommandDto command)
        {
            var at = command.Index ?? _rows.Count;
            if (at < 0 || at > _rows.Count)
                return $"Cannot insert at row {at} (the file has {_rows.Count} rows).";

            // A file with nothing in it declares no columns, so the FIRST row to arrive establishes
            // them - the same thing addLanguage does, just implied rather than asked for. Without
            // this the values had nowhere to go: the composer writes one column per DECLARED
            // language, so seeding an empty CSV produced a bare list of keys with every value
            // dropped, and the first of those keys was then read back as the header row.
            //
            // Only while there are none. Once the file has columns, a value for an undeclared
            // language is a mistake to refuse rather than a column to invent silently - adding one
            // here would let a stray language widen the file without anyone asking for it.
            if (_languages.Count == 0)
                foreach (var value in command.Values ?? [])
                    if (!_languages.Any(l =>
                            string.Equals(l, value.Language, StringComparison.OrdinalIgnoreCase)))
                        _languages.Add(value.Language);

            _rows.Insert(at, new RowState
            {
                Key = command.Key ?? string.Empty,
                Values = [.. command.Values ?? []],
                Source = string.Empty,
                Leading = string.Empty,
                Modified = true
            });

            return null;
        }

        private string? DeleteRow(LocEditCommandDto command)
        {
            if (Resolve(command, out _, out var error) is false) return error;

            _rows.RemoveAt(command.Index!.Value);
            return null;
        }

        private string? MoveRow(LocEditCommandDto command)
        {
            if (Resolve(command, out var row, out var error) is false) return error;

            var to = command.ToIndex ?? 0;
            if (to < 0 || to >= _rows.Count)
                return $"Cannot move to row {to} (the file has {_rows.Count} rows).";

            _rows.RemoveAt(command.Index!.Value);
            _rows.Insert(to, row!);
            return null;
        }

        private string? AddLanguage(LocEditCommandDto command)
        {
            if (string.IsNullOrWhiteSpace(command.Language)) return "No language given.";

            if (!SupportsMultipleLanguages(_extension))
                return $"{_extension} files are single-language and cannot take another column.";

            if (_languages.Any(l => string.Equals(l, command.Language, StringComparison.OrdinalIgnoreCase)))
                return $"'{command.Language}' is already a language in this file.";

            _languages.Add(command.Language);
            _preamble = $"{_preamble},{command.Language}";

            // Every row gains a cell, so no row's original text is valid any more.
            foreach (var row in _rows)
            {
                row.Values.Add(new LocValueDto(command.Language, string.Empty));
                row.Modified = true;
            }

            return null;
        }

        /// <summary>Bounds-checks the index and enforces the caller's expected-key assertion.</summary>
        private bool Resolve(LocEditCommandDto command, out RowState? row, out string? error)
        {
            row = null;
            error = null;

            if (command.Index is not { } index || index < 0 || index >= _rows.Count)
            {
                error = $"Row {command.Index} is out of range (the file has {_rows.Count} rows).";
                return false;
            }

            row = _rows[index];

            if (command.ExpectedKey is { } expected
                && !string.Equals(row.Key, expected, StringComparison.Ordinal))
            {
                error = $"Row {index} is no longer '{expected}'. Reload before editing again.";
                return false;
            }

            return true;
        }
    }
}
