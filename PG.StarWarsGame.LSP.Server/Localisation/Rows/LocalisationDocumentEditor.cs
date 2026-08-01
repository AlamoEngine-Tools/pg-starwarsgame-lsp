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

    public async Task<LocalisationEditResult> ApplyToFileAsync(
        string filePath, IReadOnlyList<LocEditCommandDto> commands, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var extension = fs.Path.GetExtension(filePath).ToLowerInvariant();

        if (extension != ".dat")
        {
            var original = await fs.File.ReadAllTextAsync(filePath, ct);
            var composed = Apply(original, extension, commands);
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

        return Apply(fs.File.ReadAllText(filePath), extension, commands);
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

        return KeyedCommandTranslator.Translate(document, commands);
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
        // The key is hashed exactly as the DAT builders do - same service, same encoding - so an
        // edited row's checksum matches what the game expects to look up.
        var entries = rows.Select(r => new DatStringEntry(
            r.Key,
            _hashing.GetCrc32(r.Key, Encoding.ASCII),
            r.Values.FirstOrDefault(v => v.Language == language)?.Value ?? string.Empty));

        var fileType = sortOrder == DatFileType.OrderedByCrc32
            ? DatFileType.OrderedByCrc32
            : DatFileType.NotOrdered;

        using (var stream = fs.File.Create(filePath))
        {
            _datFileService.CreateDatFile(stream, entries, fileType);
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
        string originalText, string extension, IReadOnlyList<LocEditCommandDto> commands)
    {
        // XML is composed through XDocument rather than through row slices: with preserved
        // whitespace it already re-serialises untouched markup unchanged, and rebuilding elements
        // from slices would mean reimplementing namespace and escaping rules.
        if (extension == ".xml") return ApplyXml(originalText, commands);

        LocDocument document;
        try
        {
            document = _reader.Read(originalText, extension);
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

        var declaration = document.Declaration is { } d ? d + DetectLineEnding(text) : string.Empty;
        return LocalisationEditResult.Ok(declaration + Serialise(document.Root));
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
                var translation = element
                    .Elements(ns + "TranslationData")
                    .Elements(ns + "Translation")
                    .FirstOrDefault(e => string.Equals(
                        e.Attribute("Language")?.Value, command.Language, StringComparison.OrdinalIgnoreCase));

                if (translation is null) return $"Row {index} has no '{command.Language}' translation.";
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
            if (_preamble.Length > 0) builder.Append(_preamble).Append(_lineEnding);

            foreach (var row in _rows)
            {
                builder.Append(row.Leading);
                builder.Append(row.Modified ? Serialise(row) : row.Source);
                builder.Append(_lineEnding);
            }

            return builder.ToString();
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

            if (_extension == ".properties")
                return ".properties files are single-language and cannot take another column.";

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
