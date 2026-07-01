// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class LocalisationEntryWriter : ILocalisationEntryWriter
{
    private const string XmlNs = "urn:alamoenginetools:localisation:v1";

    private readonly IFileHelper _fileHelper;
    private readonly ILogger<LocalisationEntryWriter> _logger;

    public LocalisationEntryWriter(IFileHelper fileHelper, ILogger<LocalisationEntryWriter> logger)
    {
        _fileHelper = fileHelper;
        _logger = logger;
    }

    public async Task<bool> ExistsAsync(string filePath, string key, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var ext = fs.Path.GetExtension(filePath).ToLowerInvariant();
        var content = await fs.File.ReadAllTextAsync(filePath, ct);

        return ext switch
        {
            ".csv" => FindCsvRowIndex(SplitLines(content), key) >= 0,
            ".xml" => TryParseXml(content, out var xdoc) && FindXmlElement(xdoc!, key) is not null,
            ".properties" => FindNlsLineIndex(SplitLines(content), key) >= 0,
            _ => false
        };
    }

    public async Task<bool> UpsertAsync(
        string filePath, string key, IReadOnlyDictionary<string, string>? translations, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var ext = fs.Path.GetExtension(filePath).ToLowerInvariant();

        switch (ext)
        {
            case ".csv":
                return await UpsertCsvAsync(filePath, key, translations, ct);
            case ".xml":
                return await UpsertXmlAsync(filePath, key, translations, ct);
            case ".properties":
                return await UpsertNlsAsync(filePath, key, translations, ct);
            default:
                _logger.LogWarning("Cannot upsert '{Key}': unsupported file format '{Path}'.", key, filePath);
                return false;
        }
    }

    public async Task<bool> DeleteAsync(string filePath, string key, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var ext = fs.Path.GetExtension(filePath).ToLowerInvariant();

        switch (ext)
        {
            case ".csv":
                return await DeleteCsvAsync(filePath, key, ct);
            case ".xml":
                return await DeleteXmlAsync(filePath, key, ct);
            case ".properties":
                return await DeleteNlsAsync(filePath, key, ct);
            default:
                _logger.LogWarning("Cannot delete '{Key}': unsupported file format '{Path}'.", key, filePath);
                return false;
        }
    }

    public async Task<bool> AddLanguageAsync(string filePath, string language, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var ext = fs.Path.GetExtension(filePath).ToLowerInvariant();

        switch (ext)
        {
            case ".csv":
                return await AddLanguageCsvAsync(filePath, language, ct);
            case ".xml":
                return await AddLanguageXmlAsync(filePath, language, ct);
            case ".properties":
                _logger.LogWarning(
                    "Cannot add language '{Language}' to '{Path}': .properties files are single-language.",
                    language, filePath);
                return false;
            default:
                _logger.LogWarning("Cannot add language '{Language}': unsupported file format '{Path}'.",
                    language, filePath);
                return false;
        }
    }

    // ── CSV ──────────────────────────────────────────────────────────────────

    private async Task<bool> UpsertCsvAsync(
        string filePath, string key, IReadOnlyDictionary<string, string>? translations, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var lines = SplitLines(await fs.File.ReadAllTextAsync(filePath, ct));
        if (lines.Count == 0) return false;

        var columns = lines[0].Split(',');
        var newRow = BuildCsvRow(key, columns, translations);

        var rowIndex = FindCsvRowIndex(lines, key);
        if (rowIndex >= 0) lines[rowIndex] = newRow;
        else lines.Add(newRow);

        await fs.File.WriteAllTextAsync(filePath, JoinLines(lines), ct);
        return true;
    }

    private async Task<bool> DeleteCsvAsync(string filePath, string key, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var lines = SplitLines(await fs.File.ReadAllTextAsync(filePath, ct));
        var rowIndex = FindCsvRowIndex(lines, key);
        if (rowIndex < 0) return false;

        lines.RemoveAt(rowIndex);
        await fs.File.WriteAllTextAsync(filePath, JoinLines(lines), ct);
        return true;
    }

    private async Task<bool> AddLanguageCsvAsync(string filePath, string language, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var lines = SplitLines(await fs.File.ReadAllTextAsync(filePath, ct));
        if (lines.Count == 0) return false;

        var columns = lines[0].Split(',');
        if (columns.Any(c => string.Equals(c, language, StringComparison.OrdinalIgnoreCase)))
            return false;

        lines[0] += "," + language;
        for (var i = 1; i < lines.Count; i++)
            if (lines[i].Length > 0)
                lines[i] += ",";

        await fs.File.WriteAllTextAsync(filePath, JoinLines(lines), ct);
        return true;
    }

    private static int FindCsvRowIndex(List<string> lines, string key)
    {
        for (var i = 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            var firstComma = line.IndexOf(',');
            var rowKey = firstComma >= 0 ? line[..firstComma] : line;
            if (string.Equals(rowKey, key, StringComparison.Ordinal)) return i;
        }

        return -1;
    }

    private static string BuildCsvRow(string key, string[] columns, IReadOnlyDictionary<string, string>? translations)
    {
        var cells = new string[columns.Length];
        cells[0] = key;
        for (var i = 1; i < columns.Length; i++)
        {
            var lang = columns[i];
            cells[i] = translations is not null && translations.TryGetValue(lang, out var value)
                ? EscapeCsvField(value)
                : string.Empty;
        }

        return string.Join(",", cells);
    }

    private static string EscapeCsvField(string value)
    {
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    // ── NLS (.properties) ────────────────────────────────────────────────────

    private async Task<bool> UpsertNlsAsync(
        string filePath, string key, IReadOnlyDictionary<string, string>? translations, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var lines = SplitLines(await fs.File.ReadAllTextAsync(filePath, ct));

        var value = translations is { Count: > 0 } ? translations.Values.First() : string.Empty;
        var newLine = $"{key}={value}";

        var index = FindNlsLineIndex(lines, key);
        if (index >= 0) lines[index] = newLine;
        else lines.Add(newLine);

        await fs.File.WriteAllTextAsync(filePath, JoinLines(lines), ct);
        return true;
    }

    private async Task<bool> DeleteNlsAsync(string filePath, string key, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var lines = SplitLines(await fs.File.ReadAllTextAsync(filePath, ct));
        var index = FindNlsLineIndex(lines, key);
        if (index < 0) return false;

        lines.RemoveAt(index);
        await fs.File.WriteAllTextAsync(filePath, JoinLines(lines), ct);
        return true;
    }

    private static int FindNlsLineIndex(List<string> lines, string key)
    {
        var prefix = key + "=";
        return lines.FindIndex(l => l.StartsWith(prefix, StringComparison.Ordinal));
    }

    // ── XML ──────────────────────────────────────────────────────────────────

    private async Task<bool> UpsertXmlAsync(
        string filePath, string key, IReadOnlyDictionary<string, string>? translations, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var content = await fs.File.ReadAllTextAsync(filePath, ct);
        if (!TryParseXml(content, out var xdoc)) return false;

        var ns = XNamespace.Get(XmlNs);
        var root = xdoc!.Root;
        if (root is null) return false;

        var translationData = new XElement(ns + "TranslationData");
        if (translations is not null)
            foreach (var (lang, value) in translations)
                translationData.Add(new XElement(ns + "Translation", new XAttribute("Language", lang), value));

        var existing = FindXmlElement(xdoc, key);
        if (existing is not null)
        {
            existing.Element(ns + "TranslationData")?.Remove();
            existing.Add(translationData);
        }
        else
        {
            root.Add(new XElement(ns + "Localisation", new XAttribute("key", key), translationData));
        }

        await SaveXmlAsync(fs, filePath, xdoc, ct);
        return true;
    }

    private async Task<bool> DeleteXmlAsync(string filePath, string key, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var content = await fs.File.ReadAllTextAsync(filePath, ct);
        if (!TryParseXml(content, out var xdoc)) return false;

        var element = FindXmlElement(xdoc!, key);
        if (element is null) return false;

        element.Remove();
        await SaveXmlAsync(fs, filePath, xdoc!, ct);
        return true;
    }

    private async Task<bool> AddLanguageXmlAsync(string filePath, string language, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var content = await fs.File.ReadAllTextAsync(filePath, ct);
        if (!TryParseXml(content, out var xdoc)) return false;

        var ns = XNamespace.Get(XmlNs);
        var root = xdoc!.Root;
        if (root is null) return false;

        var alreadyPresent = root.Elements(ns + "Localisation")
            .Elements(ns + "TranslationData")
            .Elements(ns + "Translation")
            .Any(e => string.Equals(e.Attribute("Language")?.Value, language, StringComparison.OrdinalIgnoreCase));
        if (alreadyPresent) return false;

        foreach (var localisation in root.Elements(ns + "Localisation"))
        {
            var translationData = localisation.Element(ns + "TranslationData");
            if (translationData is null)
            {
                translationData = new XElement(ns + "TranslationData");
                localisation.Add(translationData);
            }

            translationData.Add(new XElement(ns + "Translation", new XAttribute("Language", language), string.Empty));
        }

        await SaveXmlAsync(fs, filePath, xdoc, ct);
        return true;
    }

    private static XElement? FindXmlElement(XDocument xdoc, string key)
    {
        var ns = XNamespace.Get(XmlNs);
        return xdoc.Root?.Elements(ns + "Localisation").FirstOrDefault(e => e.Attribute("key")?.Value == key);
    }

    private bool TryParseXml(string content, out XDocument? xdoc)
    {
        try
        {
            xdoc = XDocument.Parse(content);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse localisation XML.");
            xdoc = null;
            return false;
        }
    }

    private static async Task SaveXmlAsync(
        System.IO.Abstractions.IFileSystem fs, string filePath, XDocument xdoc, CancellationToken ct)
    {
        var sb = new StringBuilder();
        await using (var writer = new StringWriter(sb))
        {
            xdoc.Save(writer);
        }

        await fs.File.WriteAllTextAsync(filePath, sb.ToString(), ct);
    }

    // ── shared line helpers ──────────────────────────────────────────────────

    // Normalizes to \n, splits into logical lines, and drops the single trailing empty entry
    // produced when the file ends with a newline — so callers never have to special-case it.
    private static List<string> SplitLines(string text)
    {
        var normalized = text.Replace("\r\n", "\n");
        var lines = normalized.Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static string JoinLines(List<string> lines)
    {
        return string.Join("\n", lines) + "\n";
    }
}
