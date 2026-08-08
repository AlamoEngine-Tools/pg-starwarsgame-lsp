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
using PG.StarWarsGame.Localisation.Languages;
using PG.StarWarsGame.Localisation.Services;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;

namespace PG.StarWarsGame.LSP.Server.Commands;

// One-time migration action: adopts a directory of *existing* translation files (CSV/XML/NLS)
// into the .pgproj's "localisation" node - either by pure registration (source and target format
// match, files untouched) or by converting the source files into a new target format/directory.
// Complements InitLocalisationProjectCommandHandler, which only ever creates a fresh
// baseline-seeded file and has no path for adopting content that already exists.
public sealed class ImportLocalisationProjectCommandHandler : ExecuteCommandHandlerBase
{
    public const string CommandName = "aet-eaw-edit.lsp.importLocalisationProject";
    private readonly ILspConfigurationProvider _config;

    private readonly ICsvTranslationImporter _csvImporter;
    private readonly IDatFileService _datFileService;
    private readonly IDatTranslationImporter _datImporter;
    private readonly ITranslationDatabaseFactory _factory;
    private readonly IFileHelper _fileHelper;
    private readonly IModProjectFileWriter _fileWriter;
    private readonly ILanguageService _langService;
    private readonly ILogger<ImportLocalisationProjectCommandHandler> _logger;
    private readonly IPropertiesTranslationImporter _nlsImporter;
    private readonly IUserNotifier _notifier;
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
        ILogger<ImportLocalisationProjectCommandHandler> logger,
        ILspConfigurationProvider config,
        IUserNotifier notifier)
    {
        _notifier = notifier;
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
        _config = config;
    }

    public override async Task<Unit> Handle(ExecuteCommandParams request, CancellationToken ct)
    {
        if (LocalisationFeatureDisabled.Rejection(_config) is { } rejection)
        {
            Fail(rejection);
            return Unit.Value;
        }

        if (request.Arguments?.FirstOrDefault() is not JObject args)
        {
            Fail("Cannot import localisation files: the command was invoked without any settings to " +
                 "import with. Run it from the Localisation view rather than directly.");
            return Unit.Value;
        }

        var sourceFormat = args.Value<string>("sourceFormat");
        var sourceDirectory = args.Value<string>("sourceDirectory");
        var targetFormat = args.Value<string>("targetFormat");
        var targetDirectory = args.Value<string>("targetDirectory"); // required only when converting

        if (string.IsNullOrWhiteSpace(sourceFormat) || string.IsNullOrWhiteSpace(sourceDirectory) ||
            string.IsNullOrWhiteSpace(targetFormat))
        {
            Fail("Cannot import localisation files: the source format, source folder and target " +
                 "format are all required.");
            return Unit.Value;
        }

        var rootLayer = _reloadService.LastWorkspaceConfig?.Layers
            .OrderByDescending(l => l.Rank).FirstOrDefault();
        if (rootLayer?.ProjectPath is not { } pgprojPath)
        {
            Fail("Cannot import localisation files: no .pgproj file was found in this workspace, so " +
                 "there is nowhere to record the imported localisation settings.");
            return Unit.Value;
        }

        var fs = _fileHelper.FileSystem;
        if (!fs.Directory.Exists(sourceDirectory))
        {
            Fail($"Cannot import localisation files: the folder '{sourceDirectory}' does not exist.");
            return Unit.Value;
        }

        var sourceExt = ResourceTypeToExtension(sourceFormat);
        var sourceFiles = fs.Directory
            .EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly)
            // Filtered rather than globbed - see LocalisationLoader.EnumerateFromTextRoots.
            .Where(p => string.Equals(
                fs.Path.GetExtension(p), sourceExt, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sourceFiles.Count == 0)
        {
            Fail($"Nothing to import: no {sourceExt} files were found in '{sourceDirectory}'. Check " +
                 "that the folder and the source format match what is actually there.");
            return Unit.Value;
        }

        var pgprojDir = fs.Path.GetDirectoryName(pgprojPath)!;
        var sameFormat = string.Equals(sourceFormat, targetFormat, StringComparison.OrdinalIgnoreCase);

        string relativeDirectory;
        string outcome;
        if (sameFormat)
        {
            // Pure registration - the user's existing files are left untouched.
            var relative = fs.Path.GetRelativePath(pgprojDir, sourceDirectory);
            if (fs.Path.IsPathRooted(relative))
            {
                Fail($"Cannot import localisation files: '{sourceDirectory}' is on a different drive " +
                     "or root from the .pgproj, so it cannot be recorded as a relative path. Copy the " +
                     "files inside the project folder first.");
                return Unit.Value;
            }

            relativeDirectory = relative.Replace('\\', '/').ToLowerInvariant();
            outcome = $"Localisation project imported: registered {sourceFiles.Count} {sourceExt} " +
                      $"file(s) in '{relativeDirectory}'. The files themselves were not changed.";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                Fail($"Cannot convert {sourceFormat} to {targetFormat}: no target folder was given for " +
                     "the converted file.");
                return Unit.Value;
            }

            var targetFileName = LocalisationFormatUtility.ToSeedFileName(
                targetFormat,
                LocalisationFileNameLanguageResolver
                    .Configured(_langService, _config.Current.Localisation).LanguageIdentifier);
            if (targetFileName is null)
            {
                Fail($"Cannot import localisation files: '{targetFormat}' is not a format that can be " +
                     "written. Use CSV, XML or NLS.");
                return Unit.Value;
            }

            var absoluteTargetDir = fs.Path.Combine(pgprojDir, targetDirectory);
            var targetPath = fs.Path.Combine(absoluteTargetDir, targetFileName);
            if (fs.File.Exists(targetPath))
            {
                Fail($"Nothing was imported: '{targetPath}' already exists and was left untouched. " +
                     "Delete or move it first, or choose a different target folder.");
                return Unit.Value;
            }

            var languages = _langService.OfficiallySupported();
            var merged = _factory.CreateKeyed(languages);

            // A file that cannot be read is skipped rather than failing the whole import - one
            // corrupt file among twenty should not block the other nineteen. But skipping silently
            // is how an import "succeeds" into an empty file, so the count is reported below.
            var skipped = 0;
            foreach (var path in sourceFiles)
                if (!await ImportFileAsync(path, sourceFormat, merged, ct))
                    skipped++;

            if (skipped > 0)
                Fail($"{skipped} of {sourceFiles.Count} file(s) in '{sourceDirectory}' could not be " +
                     "read and were left out of the import. See the EaWEdit LSP output for which.");

            var writtenPath = await _seedWriter.WriteAsync(merged, targetFormat, absoluteTargetDir, ct);
            if (writtenPath is null)
            {
                Fail($"Cannot import localisation files: '{targetFormat}' is not a format that can be " +
                     "written. Use CSV, XML or NLS.");
                return Unit.Value;
            }

            _logger.LogInformation(
                "aet-eaw-edit.lsp.importLocalisationProject: converted {Count} file(s) from {Source} to '{Path}'.",
                sourceFiles.Count, sourceFormat, writtenPath);

            relativeDirectory = targetDirectory;
            outcome = $"Localisation project imported: converted {sourceFiles.Count - skipped} " +
                      $"{sourceFormat} file(s) into '{writtenPath}'.";
        }

        await _fileWriter.SetLocalisationAsync(pgprojPath, targetFormat.ToUpperInvariant(), relativeDirectory, ct);
        await _reloadService.ReloadAsync(ct);

        _logger.LogInformation(
            "aet-eaw-edit.lsp.importLocalisationProject: registered '{Dir}' ({Format}) with '{Pgproj}'.",
            relativeDirectory, targetFormat, pgprojPath);

        _notifier.ShowInfo(outcome);
        return Unit.Value;
    }

    /// <summary>
    ///     Reports a reason this command did nothing (or did less than asked), to the user and to
    ///     the log. See the same helper on
    ///     <see cref="InitLocalisationProjectCommandHandler" /> for why it exists.
    /// </summary>
    private void Fail(string message)
    {
        _logger.LogWarning("{Cmd}: {Reason}", CommandName, message);
        _notifier.ShowError(message);
    }

    /// <summary>Reads one source file into <paramref name="db" />. False if it had to be skipped.</summary>
    private async Task<bool> ImportFileAsync(
        string path, string format, IKeyedTranslationDatabase db, CancellationToken ct)
    {
        // DAT is binary - never goes through the text-read path below. It's also one file per
        // language with no self-describing language tag, so the language comes from the file name.
        if (string.Equals(format, "dat", StringComparison.OrdinalIgnoreCase))
        {
            if (!LocalisationFileNameLanguageResolver.TryResolve(path, _langService, out var language))
            {
                _logger.LogWarning(
                    "aet-eaw-edit.lsp.importLocalisationProject: could not determine a language from DAT " +
                    "file name '{Path}' (expected '..._<LANGUAGE>.dat'); skipping.", path);
                return false;
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
                return false;
            }

            return true;
        }

        string content;
        try
        {
            content = await _fileHelper.FileSystem.File.ReadAllTextAsync(path, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "aet-eaw-edit.lsp.importLocalisationProject: could not read '{Path}'.", path);
            return false;
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
                        _nlsImporter.Import(reader, NlsLanguageOf(path), db);
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "aet-eaw-edit.lsp.importLocalisationProject: failed to import '{Path}'.", path);
            return false;
        }

        return true;
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

    /// <summary>
    ///     The language a <c>.properties</c> source holds. NLS names its language in the file name and
    ///     nowhere else, so a file that does not name one falls back to the workspace's configured game
    ///     language rather than being assumed to be the service default.
    /// </summary>
    private IAlamoLanguageDefinition NlsLanguageOf(string path)
    {
        return LocalisationFileNameLanguageResolver.Resolve(
            path, _langService,
            LocalisationFileNameLanguageResolver.Configured(_langService, _config.Current.Localisation),
            out _);
    }
}