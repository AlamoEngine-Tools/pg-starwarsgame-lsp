// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Localisation.Rows;
using PG.StarWarsGame.LSP.Localisation;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Reads one localisation file into the editor's row model.
///     <para>
///         Reads the file rather than serving the loader's databases: those hold a single language,
///         merge layers together, and drop credits files entirely - all correct for the translation
///         index, all wrong for editing one file as it is on disk.
///     </para>
/// </summary>
public sealed class GetLocalisationRowsHandler
    : IJsonRpcRequestHandler<GetLocalisationRowsParams, GetLocalisationRowsResult>
{
    private readonly IMessageCatalog _messages;
    private readonly IFileHelper _fileHelper;
    private readonly ILogger<GetLocalisationRowsHandler> _logger;
    private readonly ILocalisationProjectRegistry _registry;
    private readonly ILocalisationRowReader _rowReader;

    public GetLocalisationRowsHandler(
        ILocalisationRowReader rowReader,
        ILocalisationProjectRegistry registry,
        IFileHelper fileHelper,
        ILogger<GetLocalisationRowsHandler> logger,
        IMessageCatalog messages)
    {
        _rowReader = rowReader;
        _registry = registry;
        _fileHelper = fileHelper;
        _logger = logger;
        _messages = messages;
    }

    public async Task<GetLocalisationRowsResult> Handle(
        GetLocalisationRowsParams request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectFilePath))
            return Failure(_messages.Format("LocGetRows_NoProjectFilePathProvided"));

        var fs = _fileHelper.FileSystem;
        if (!fs.File.Exists(request.ProjectFilePath))
            return Failure(
                _messages.Format("LocGetRows_FileNotFound", request.ProjectFilePath));

        var content = await fs.File.ReadAllTextAsync(request.ProjectFilePath, ct);
        var extension = fs.Path.GetExtension(request.ProjectFilePath).ToLowerInvariant();
        var category = ResolveCategory(request.ProjectFilePath);

        LocDocument document;
        try
        {
            // Through the file reader, not the text one: .dat is binary and loads from a path.
            document = _rowReader.ReadFile(request.ProjectFilePath);
        }
        catch (NotSupportedException)
        {
            return Failure(_messages.Format("LocGetRows_UnsupportedFormat", extension));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse localisation file '{Path}'.", request.ProjectFilePath);
            return Failure(_messages.Format("LocGetRows_FailedParseFile", ex.Message));
        }

        // Source and Leading exist for the save path; sending them would multiply the payload of a
        // 19k-row file for data the client never reads.
        var rows = document.Rows
            .Select(r => new LocRowDto(r.Index, r.Key, r.Values))
            .ToList();

        return new GetLocalisationRowsResult(
            rows,
            document.Languages,
            LocalisationContentHash.Compute(content),
            category,
            category == LocCategory.Credits,
            // Format only - credits included. The engine loads the crawl from
            // creditstext_<LANGUAGE>.dat, but that constrains the DAT export, not the file being
            // edited: ExportLocalisationToDatHandler writes one CreditsText_<LANGUAGE>.dat per
            // language found in the source, which is how one credits file produces every crawl.
            CanAddLanguage: LocalisationDocumentEditor.SupportsMultipleLanguages(extension),
            AddLanguageCreatesFile:
            LocalisationFileNameLanguageResolver.CarriesLanguageInFileName(extension));
    }

    /// <summary>
    ///     The category the loader already worked out, which is the only place the project's own
    ///     credits settings are known. A file outside the registry - not under any text root - falls
    ///     back to the naming convention rather than being silently treated as text.
    /// </summary>
    private string ResolveCategory(string filePath)
    {
        var normalised = _fileHelper.NormalizeUri(filePath);

        foreach (var project in _registry.Projects)
            if (string.Equals(_fileHelper.NormalizeUri(project.FilePath), normalised,
                    StringComparison.OrdinalIgnoreCase))
                return project.Category;

        return CreditsFileClassifier.Classify(filePath, null);
    }

    private static GetLocalisationRowsResult Failure(string error)
    {
        return new GetLocalisationRowsResult([], [], string.Empty, LocCategory.Text, false, error);
    }
}