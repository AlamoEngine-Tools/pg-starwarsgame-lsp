// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using System.Xml.Linq;
using MediatR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Commands;

public sealed class CreateLocalisationKeyCommandHandler : ExecuteCommandHandlerBase
{
    public const string CommandName = "aet-eaw-edit.lsp.createLocalisationKey";

    private const string XmlNs = "http://www.example.org/eaw-translation/";

    private readonly IFileHelper _fileHelper;
    private readonly IModProjectReloadService _reloadService;
    private readonly ILogger<CreateLocalisationKeyCommandHandler> _logger;

    public CreateLocalisationKeyCommandHandler(
        IFileHelper fileHelper,
        IModProjectReloadService reloadService,
        ILogger<CreateLocalisationKeyCommandHandler> logger)
    {
        _fileHelper = fileHelper;
        _reloadService = reloadService;
        _logger = logger;
    }

    public override async Task<Unit> Handle(ExecuteCommandParams request, CancellationToken ct)
    {
        if (request.Arguments is null || request.Arguments.Count < 2)
        {
            _logger.LogWarning("{Cmd}: missing arguments.", CommandName);
            return Unit.Value;
        }

        var keyName = request.Arguments[0]?.Value<string>();
        var filePath = request.Arguments[1]?.Value<string>();

        if (string.IsNullOrWhiteSpace(keyName))
        {
            _logger.LogWarning("{Cmd}: missing key name.", CommandName);
            return Unit.Value;
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            _logger.LogWarning("{Cmd}: missing file path.", CommandName);
            return Unit.Value;
        }

        var translations = request.Arguments.Count > 2
            ? request.Arguments[2] as JObject
            : null;

        var fs = _fileHelper.FileSystem;
        if (!fs.File.Exists(filePath))
        {
            _logger.LogWarning("{Cmd}: file '{Path}' not found.", CommandName, filePath);
            return Unit.Value;
        }

        var ext = fs.Path.GetExtension(filePath).ToLowerInvariant();
        var written = ext switch
        {
            ".csv" => await AppendCsv(filePath, keyName, translations, ct),
            ".properties" => await AppendNls(filePath, keyName, translations, ct),
            ".xml" => await AppendXml(filePath, keyName, translations, ct),
            _ => false
        };

        if (written)
            await _reloadService.ReloadAsync(ct);

        return Unit.Value;
    }

    private async Task<bool> AppendCsv(
        string filePath, string keyName, JObject? translations, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var existing = await fs.File.ReadAllTextAsync(filePath, ct);
        var lines = existing.Split('\n');

        if (lines.Length == 0) return false;

        var header = lines[0].TrimEnd('\r');
        var columns = header.Split(',');

        // duplicate check
        foreach (var line in lines.Skip(1))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) continue;
            var firstComma = trimmed.IndexOf(',');
            var rowKey = firstComma >= 0 ? trimmed[..firstComma] : trimmed;
            if (string.Equals(rowKey, keyName, StringComparison.Ordinal))
            {
                _logger.LogWarning("{Cmd}: key '{Key}' already exists in '{Path}'.", CommandName, keyName, filePath);
                return false;
            }
        }

        // build new row: key column first, then one cell per language column (in header order)
        var cells = new string[columns.Length];
        cells[0] = keyName;
        for (var i = 1; i < columns.Length; i++)
        {
            var lang = columns[i];
            cells[i] = translations?[lang]?.Value<string>() ?? string.Empty;
        }

        var newRow = string.Join(",", cells);
        var appended = existing.TrimEnd('\n', '\r') + "\n" + newRow + "\n";
        await fs.File.WriteAllTextAsync(filePath, appended, ct);
        return true;
    }

    private async Task<bool> AppendNls(
        string filePath, string keyName, JObject? translations, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var existing = await fs.File.ReadAllTextAsync(filePath, ct);

        // duplicate check
        foreach (var line in existing.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith(keyName + "=", StringComparison.Ordinal))
            {
                _logger.LogWarning("{Cmd}: key '{Key}' already exists in '{Path}'.", CommandName, keyName, filePath);
                return false;
            }
        }

        var value = translations?.Properties().FirstOrDefault()?.Value.Value<string>() ?? string.Empty;
        var newLine = $"{keyName}={value}";
        var appended = existing.TrimEnd('\n', '\r') + "\n" + newLine + "\n";
        await fs.File.WriteAllTextAsync(filePath, appended, ct);
        return true;
    }

    private async Task<bool> AppendXml(
        string filePath, string keyName, JObject? translations, CancellationToken ct)
    {
        var fs = _fileHelper.FileSystem;
        var existing = await fs.File.ReadAllTextAsync(filePath, ct);

        XDocument xdoc;
        try
        {
            xdoc = XDocument.Parse(existing);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Cmd}: failed to parse XML at '{Path}'.", CommandName, filePath);
            return false;
        }

        var ns = XNamespace.Get(XmlNs);
        var root = xdoc.Root;
        if (root is null) return false;

        // duplicate check
        if (root.Elements(ns + "Localisation").Any(e => e.Attribute("key")?.Value == keyName))
        {
            _logger.LogWarning("{Cmd}: key '{Key}' already exists in '{Path}'.", CommandName, keyName, filePath);
            return false;
        }

        // build element
        var translationData = new XElement(ns + "TranslationData");
        if (translations is not null)
        {
            foreach (var prop in translations.Properties())
            {
                translationData.Add(new XElement(ns + "Translation",
                    new XAttribute("Language", prop.Name),
                    prop.Value.Value<string>() ?? string.Empty));
            }
        }

        var newEntry = new XElement(ns + "Localisation",
            new XAttribute("key", keyName),
            translationData);

        root.Add(newEntry);

        var sb = new StringBuilder();
        using var writer = new System.IO.StringWriter(sb);
        xdoc.Save(writer);
        await fs.File.WriteAllTextAsync(filePath, sb.ToString(), ct);
        return true;
    }

    protected override ExecuteCommandRegistrationOptions CreateRegistrationOptions(
        ExecuteCommandCapability capability, ClientCapabilities clientCapabilities)
    {
        return new ExecuteCommandRegistrationOptions { Commands = new Container<string>(CommandName) };
    }
}
