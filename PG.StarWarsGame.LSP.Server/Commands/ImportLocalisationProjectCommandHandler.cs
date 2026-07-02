// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Xml.Linq;
using MediatR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using PG.StarWarsGame.Files.DAT.Services;
using PG.StarWarsGame.Localisation.Data;
using PG.StarWarsGame.Localisation.IO.Csv;
using PG.StarWarsGame.Localisation.IO.Dat;
using PG.StarWarsGame.Localisation.IO.Properties;
using PG.StarWarsGame.Localisation.IO.Xml;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Commands;

// One-time migration action: adopts a directory of *existing* translation files (CSV/XML/NLS)
// into the .pgproj's "localisation" node — either by pure registration (source and target format
// match, files untouched) or by converting the source files into a new target format/directory.
// Complements InitLocalisationProjectCommandHandler, which only ever creates a fresh
// baseline-seeded file and has no path for adopting content that already exists.
public sealed class ImportLocalisationProjectCommandHandler : ExecuteCommandHandlerBase
{
    public const string CommandName = "aet-eaw-edit.lsp.importLocalisationProject";

    private readonly ICsvTranslationImporter _csvImporter;
    private readonly IDatTranslationImporter _datImporter;
    private readonly IDatFileService _datFileService;
    private readonly ITranslationDatabaseFactory _factory;
    private readonly IFileHelper _fileHelper;
    private readonly IModProjectFileWriter _fileWriter;
    private readonly ILanguageService _langService;
    private readonly ILogger<ImportLocalisationProjectCommandHandler> _logger;
    private readonly IPropertiesTranslationImporter _nlsImporter;
    private readonly IModProjectReloadService _reloadService;
    private readonly ILocalisationSeedFileWriter _seedWriter;
    private readonly IXmlTranslationImporter _xmlImporter;

    public ImportLocalisationProjectCommandHandler(
        ICsvTranslationImporter csvImporter,
        IXmlTranslationImporter xmlImporter,
        IPropertiesTranslationImporter nlsImporter,
        IDatTranslationImporter datImporter,
        IDatFileService datFileService,
        ITranslationDatabaseFactory factory,
        ILanguageService langService,
        ILocalisationSeedFileWriter seedWriter,
        IFileHelper fileHelper,
        IModProjectReloadService reloadService,
        IModProjectFileWriter fileWriter,
        ILogger<ImportLocalisationProjectCommandHandler> logger)
    {
        _csvImporter = csvImporter;
        _xmlImporter = xmlImporter;
        _nlsImporter = nlsImporter;
        _datImporter = datImporter;
        _datFileService = datFileService;
        _factory = factory;
        _langService = langService;
        _seedWriter = seedWriter;
        _fileHelper = fileHelper;
        _reloadService = reloadService;
        _fileWriter = fileWriter;
        _logger = logger;
    }

    public override async Task<Unit> Handle(ExecuteCommandParams request, CancellationToken ct)
    {
        if (request.Arguments?.FirstOrDefault() is not JObject args)
        {
            _logger.LogWarning("aet-eaw-edit.lsp.importLocalisationProject invoked without arguments.");
            return Unit.Value;
        }

        var sourceFormat = args.Value<string>("sourceFormat");
        var sourceDirectory = args.Value<string>("sourceDirectory");
        var targetFormat = args.Value<string>("targetFormat");
        var targetDirectory = args.Value<string>("targetDirectory"); // required only when converting

        if (string.IsNullOrWhiteSpace(sourceFormat) || string.IsNullOrWhiteSpace(sourceDirectory) ||
            string.IsNullOrWhiteSpace(targetFormat))
        {
            _logger.LogWarning(
                "aet-eaw-edit.lsp.importLocalisationProject: missing sourceFormat/sourceDirectory/targetFormat.");
            return Unit.Value;
        }

        var rootLayer = _reloadService.LastWorkspaceConfig?.Layers
            .OrderByDescending(l => l.Rank).FirstOrDefault();
        if (rootLayer?.ProjectPath is not { } pgprojPath)
        {
            _logger.LogWarning("aet-eaw-edit.lsp.importLocalisationProject: no .pgproj found; cannot import.");
            return Unit.Value;
        }

        var fs = _fileHelper.FileSystem;
        if (!fs.Directory.Exists(sourceDirectory))
        {
            _logger.LogWarning(
                "aet-eaw-edit.lsp.importLocalisationProject: source directory '{Dir}' does not exist.",
                sourceDirectory);
            return Unit.Value;
        }

        var sourceExt = ResourceTypeToExtension(sourceFormat);
        var sourceFiles = fs.Directory
            .EnumerateFiles(sourceDirectory, $"*{sourceExt}", SearchOption.TopDirectoryOnly)
            .ToList();
        if (sourceFiles.Count == 0)
        {
            _logger.LogWarning(
                "aet-eaw-edit.lsp.importLocalisationProject: no {Ext} files found in '{Dir}'.",
                sourceExt, sourceDirectory);
            return Unit.Value;
        }

        var pgprojDir = fs.Path.GetDirectoryName(pgprojPath)!;
        var sameFormat = string.Equals(sourceFormat, targetFormat, StringComparison.OrdinalIgnoreCase);

        string relativeDirectory;
        if (sameFormat)
        {
            // Pure registration — the user's existing files are left untouched.
            var relative = fs.Path.GetRelativePath(pgprojDir, sourceDirectory);
            if (fs.Path.IsPathRooted(relative))
            {
                _logger.LogWarning(
                    "aet-eaw-edit.lsp.importLocalisationProject: '{Dir}' cannot be expressed as a path " +
                    "relative to the .pgproj (different drive/root); cannot import.", sourceDirectory);
                return Unit.Value;
            }

            relativeDirectory = relative.Replace('\\', '/').ToLowerInvariant();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                _logger.LogWarning(
                    "aet-eaw-edit.lsp.importLocalisationProject: targetDirectory is required when " +
                    "converting from {Source} to {Target}.", sourceFormat, targetFormat);
                return Unit.Value;
            }

            var targetFileName = LocalisationFormatUtility.ToSeedFileName(targetFormat);
            if (targetFileName is null)
            {
                _logger.LogWarning(
                    "aet-eaw-edit.lsp.importLocalisationProject: unsupported target format '{Format}'.",
                    targetFormat);
                return Unit.Value;
            }

            var absoluteTargetDir = fs.Path.Combine(pgprojDir, targetDirectory);
            var targetPath = fs.Path.Combine(absoluteTargetDir, targetFileName);
            if (fs.File.Exists(targetPath))
            {
                _logger.LogWarning(
                    "aet-eaw-edit.lsp.importLocalisationProject: '{Path}' already exists; not overwriting.",
                    targetPath);
                return Unit.Value;
            }

            var languages = _langService.OfficiallySupported();
            var merged = _factory.CreateKeyed(languages);
            foreach (var path in sourceFiles)
                await ImportFileAsync(path, sourceFormat, merged, ct);

            var writtenPath = await _seedWriter.WriteAsync(merged, targetFormat, absoluteTargetDir, ct);
            if (writtenPath is null)
            {
                _logger.LogWarning(
                    "aet-eaw-edit.lsp.importLocalisationProject: unsupported target format '{Format}'.",
                    targetFormat);
                return Unit.Value;
            }

            _logger.LogInformation(
                "aet-eaw-edit.lsp.importLocalisationProject: converted {Count} file(s) from {Source} to '{Path}'.",
                sourceFiles.Count, sourceFormat, writtenPath);

            relativeDirectory = targetDirectory;
        }

        await _fileWriter.SetLocalisationAsync(pgprojPath, targetFormat.ToUpperInvariant(), relativeDirectory, ct);
        await _reloadService.ReloadAsync(ct);

        _logger.LogInformation(
            "aet-eaw-edit.lsp.importLocalisationProject: registered '{Dir}' ({Format}) with '{Pgproj}'.",
            relativeDirectory, targetFormat, pgprojPath);

        return Unit.Value;
    }

    private async Task ImportFileAsync(
        string path, string format, IKeyedTranslationDatabase db, CancellationToken ct)
    {
        // DAT is binary — never goes through the text-read path below. It's also one file per
        // language with no self-describing language tag, so the language comes from the file name.
        if (string.Equals(format, "dat", StringComparison.OrdinalIgnoreCase))
        {
            if (!DatFileNameLanguageResolver.TryResolve(path, _langService, out var language))
            {
                _logger.LogWarning(
                    "aet-eaw-edit.lsp.importLocalisationProject: could not determine a language from DAT " +
                    "file name '{Path}' (expected '..._<LANGUAGE>.dat'); skipping.", path);
                return;
            }

            try
            {
                using var datFile = _datFileService.Load(path);
                _datImporter.Import(datFile.Content, language!, db);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "aet-eaw-edit.lsp.importLocalisationProject: failed to import DAT file '{Path}'.", path);
            }

            return;
        }

        string content;
        try
        {
            content = await _fileHelper.FileSystem.File.ReadAllTextAsync(path, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "aet-eaw-edit.lsp.importLocalisationProject: could not read '{Path}'.", path);
            return;
        }

        try
        {
            switch (format.ToLowerInvariant())
            {
                case "csv":
                    using (var reader = new StringReader(content))
                    {
                        _csvImporter.Import(reader, db);
                    }

                    break;
                case "xml":
                    _xmlImporter.Import(XDocument.Parse(content), db);
                    break;
                case "nls":
                    using (var reader = new StringReader(content))
                    {
                        _nlsImporter.Import(reader, _langService.Default, db);
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "aet-eaw-edit.lsp.importLocalisationProject: failed to import '{Path}'.", path);
        }
    }

    private static string ResourceTypeToExtension(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "csv" => ".csv",
            "xml" => ".xml",
            "nls" => ".properties",
            "dat" => ".dat",
            _ => ".csv"
        };
    }

    protected override ExecuteCommandRegistrationOptions CreateRegistrationOptions(
        ExecuteCommandCapability capability, ClientCapabilities clientCapabilities)
    {
        return new ExecuteCommandRegistrationOptions { Commands = new Container<string>(CommandName) };
    }
}
