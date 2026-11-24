// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CsvHelper;
using CsvHelper.Configuration;
using PG.Commons.Hashing;
using PG.StarWarsGame.Localisation.IO.Xml;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Text;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Localisation.Rows;

/// <inheritdoc />
public sealed class LocalisationDocumentEditor : ILocalisationDocumentEditor
{
    private readonly IFileHelper _fileHelper;
    private readonly ICrc32HashingService _hashing;
    private readonly IMessageCatalog _messages;
    private readonly ILocalisationRowReader _reader;

    public LocalisationDocumentEditor(
        ILocalisationRowReader reader,
        ICrc32HashingService hashing,
        IFileHelper fileHelper,
        IMessageCatalog messages)
    {
        _messages = messages;
        _reader = reader;
        _hashing = hashing;
        _fileHelper = fileHelper;
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
                return LocalisationEditResult.Fail(0, _messages.Format("LocEditor_FailedReadFile", ex.Message));
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
        LocDocument document;
        try
        {
            document = _reader.ReadFile(filePath);
        }
        catch (Exception ex)
        {
            return ([], [], _messages.Format("LocEditor_FailedReadFile", ex.Message));
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
                return ([], [], _messages.Format("LocEditor_Change", i + 1, error));
        }

        return (rows, languages, null);
    }

    /// <summary>
    ///     Resolves key-addressed commands against the file as it currently stands.
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
            return KeyedTranslationResult.Fail(0, _messages.Format("LocEditor_UnsupportedFormat", extension));
        }

        return KeyedCommandTranslator.Translate(document, commands, _hashing, _messages);
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
            return LocalisationEditResult.Fail(0, _messages.Format("LocEditor_UnsupportedFormat", extension));
        }

        var state = new EditState(document, extension, _messages);

        for (var i = 0; i < commands.Count; i++)
            if (state.Apply(commands[i]) is { } error)
                return LocalisationEditResult.Fail(i, error);

        return LocalisationEditResult.Ok(state.Compose());
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

    /// <summary>
    ///     The row-list half of the commands, which a dry run of a DAT goes by. Text formats go through
    ///     <see cref="EditState" /> instead, which additionally tracks each row's original text.
    /// </summary>
    private string? ApplyToRows(
        List<LocRowDto> rows, LocEditCommandDto command, IReadOnlyList<string> languages)
    {
        var index = command.Index ?? -1;
        var addressed = command.Kind is "setCell" or "setKey" or "deleteRow" or "moveRow";

        if (addressed && (index < 0 || index >= rows.Count))
            return _messages.Format("LocEditor_RowOutRangeFileHas", command.Index, rows.Count);

        if (addressed && command.ExpectedKey is { } expected
                      && !string.Equals(rows[index].Key, expected, StringComparison.Ordinal))
            return _messages.Format("LocEditor_RowNoLongerReloadBefore", index, expected);

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
                    return _messages.Format("LocEditor_CannotMoveRowFileHas", to, rows.Count);
                var moved = rows[index];
                rows.RemoveAt(index);
                rows.Insert(to, moved);
                break;

            case "insertRow":
                var at = command.Index ?? rows.Count;
                if (at < 0 || at > rows.Count)
                    return _messages.Format("LocEditor_CannotInsertAtRowFile", at, rows.Count);
                rows.Insert(at, new LocRowDto(at, command.Key ?? string.Empty, [.. command.Values ?? []]));
                break;

            // A DAT holds exactly one language - the one in its filename - so a second column
            // cannot be represented.
            case "addLanguage":
                return _messages.Format("LocEditor_DatFileHoldsSingleLanguage");

            default:
                return _messages.Format("LocEditor_UnknownCommand", command.Kind);
        }

        // Positions are the addressing scheme, so they have to stay equal to the index.
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Index != i)
                rows[i] = rows[i] with { Index = i };

        return null;
    }

    // ── XML ──────────────────────────────────────────────────────────────────

    private LocalisationEditResult ApplyXml(string text, IReadOnlyList<LocEditCommandDto> commands)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex)
        {
            return LocalisationEditResult.Fail(0, _messages.Format("LocEditor_FailedParseFile", ex.Message));
        }

        // Saved in the text editor's format, the only one it opens, whatever shape the file was in.
        var format = XmlTranslationFormat.TextEditor;
        var written = XmlTranslationFormat.Of(document);
        format.Rewrite(document);
        var rows = document.Root?.Elements(format.Localisation).ToList() ?? [];

        for (var i = 0; i < commands.Count; i++)
            if (ApplyXmlCommand(commands[i], rows, format) is { } error)
                return LocalisationEditResult.Fail(i, error);

        var lineEnding = LineEndings.Detect(text);

        // A file already in that format keeps every character the batch did not ask to change:
        // the commands are written into its text where each applies, rather than the whole
        // document being serialised anew - which rewrites the declaration and every attribute's
        // quotes and drops the comments outside the root. The document above stays the judge of
        // what the batch means: a text that does not read back as the same rows is not used.
        if (ReferenceEquals(written, format)
            && XmlTranslationSplicer.Splice(text, commands, lineEnding) is { } spliced
            && HoldsTheSameRows(spliced, document, format))
            return LocalisationEditResult.Ok(spliced);

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

    /// <summary>Whether <paramref name="text" /> reads back as the rows of <paramref name="expected" />.</summary>
    private static bool HoldsTheSameRows(string text, XDocument expected, XmlTranslationFormat format)
    {
        XDocument written;
        try
        {
            written = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return false;
        }

        return RowsOf(written, format).SequenceEqual(RowsOf(expected, format));
    }

    /// <summary>Each row as its key and its translations in order, in a form two documents can be compared by.</summary>
    private static IEnumerable<(string? Key, string Translations)> RowsOf(XDocument document,
        XmlTranslationFormat format)
    {
        return (document.Root?.Elements(format.Localisation) ?? []).Select(row => (
            row.Attribute(format.Key)?.Value,
            string.Join('\0', row.Elements(format.TranslationData).Elements(format.Translation)
                .Select(t => t.Attribute(format.Language)?.Value + '\0' + t.Value))));
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

    private string? ApplyXmlCommand(LocEditCommandDto command, List<XElement> rows, XmlTranslationFormat format)
    {
        if (command.Kind == "addLanguage")
            return string.IsNullOrWhiteSpace(command.Language)
                ? _messages.Format("LocEditor_NoLanguageGiven")
                : null; // A language exists in XML only where a value does; nothing to declare.

        if (command.Index is not { } index || index < 0 || index >= rows.Count)
            return _messages.Format("LocEditor_RowOutRangeFileHas", command.Index, rows.Count);

        var element = rows[index];
        if (command.ExpectedKey is { } expected
            && !string.Equals(element.Attribute(format.Key)?.Value, expected, StringComparison.Ordinal))
            return _messages.Format("LocEditor_RowNoLongerReloadBefore", index, expected);

        switch (command.Kind)
        {
            case "setCell":
                if (string.IsNullOrWhiteSpace(command.Language)) return _messages.Format("LocEditor_NoLanguageGiven");

                var data = element.Elements(format.TranslationData).FirstOrDefault();
                if (data is null)
                {
                    data = new XElement(format.TranslationData);
                    element.Add(data);
                }

                var translation = data
                    .Elements(format.Translation)
                    .FirstOrDefault(e => string.Equals(
                        e.Attribute(format.Language)?.Value, command.Language,
                        StringComparison.OrdinalIgnoreCase));

                // A language exists in this format only where a value does - there is no column list
                // to extend, which is why addLanguage has nothing to do here. So writing a value for
                // a language the row does not carry yet is how that language comes into existence.
                // Refusing instead meant every write to a newly added language failed on save.
                if (translation is null)
                {
                    translation = new XElement(format.Translation,
                        new XAttribute(format.Language, command.Language));
                    AppendTranslation(data, translation);
                }

                translation.Value = command.Value ?? string.Empty;
                return null;

            case "setKey":
                element.SetAttributeValue(format.Key, command.Key ?? string.Empty);
                return null;

            case "deleteRow":
                element.Remove();
                rows.RemoveAt(index);
                return null;

            default:
                return _messages.Format("LocEditor_UnsupportedCommandXML", command.Kind);
        }
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
        private readonly List<string> _languages;
        private readonly string _lineEnding;
        private readonly IMessageCatalog _messages;
        private readonly List<RowState> _rows;
        private string _preamble;
        private readonly string? _trailer;

        public EditState(LocDocument document, string extension, IMessageCatalog messages)
        {
            _extension = extension;
            _messages = messages;
            _lineEnding = document.LineEnding;
            _preamble = document.Preamble;
            _trailer = document.Trailer;
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
                default: return _messages.Format("LocEditor_UnknownCommand", command.Kind);
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

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                builder.Append(row.Leading);
                builder.Append(row.Modified ? Serialise(row) : row.Source);
                // After the last row comes what the file had there: its line ending or none, and
                // whatever followed. A document not read from a text ends on one line ending.
                builder.Append(i < _rows.Count - 1 ? _lineEnding : _trailer ?? _lineEnding);
            }

            // With every row gone, what followed the last one still belongs to the file; the line
            // ending it began with went with the row.
            if (_rows.Count == 0 && _trailer is not null)
                builder.Append(_trailer.StartsWith("\r\n", StringComparison.Ordinal) ? _trailer[2..]
                    : _trailer.StartsWith('\n') ? _trailer[1..]
                    : _trailer);

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
            if (index < 0) return _messages.Format("LocEditor_NotLanguageThisFile", command.Language);

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
                return _messages.Format("LocEditor_CannotInsertAtRowFile", at, _rows.Count);

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
                return _messages.Format("LocEditor_CannotMoveRowFileHas", to, _rows.Count);

            _rows.RemoveAt(command.Index!.Value);
            _rows.Insert(to, row!);
            return null;
        }

        private string? AddLanguage(LocEditCommandDto command)
        {
            if (string.IsNullOrWhiteSpace(command.Language)) return _messages.Format("LocEditor_NoLanguageGiven");

            if (!SupportsMultipleLanguages(_extension))
                return _messages.Format("LocEditor_FilesAreSingleLanguageCannot", _extension);

            if (_languages.Any(l => string.Equals(l, command.Language, StringComparison.OrdinalIgnoreCase)))
                return _messages.Format("LocEditor_AlreadyLanguageThisFile", command.Language);

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
                error = _messages.Format("LocEditor_RowOutRangeFileHas", command.Index, _rows.Count);
                return false;
            }

            row = _rows[index];

            if (command.ExpectedKey is { } expected
                && !string.Equals(row.Key, expected, StringComparison.Ordinal))
            {
                error = _messages.Format("LocEditor_RowNoLongerReloadBefore", index, expected);
                return false;
            }

            return true;
        }
    }
}