// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Dry-runs a staged batch of credits edits: composes it, discards the result, and reports what
///     is wrong.
///     <para>
///         There are no key rules to apply. Duplicate keys are the format - the key is a formatting
///         directive repeated on hundreds of rows - and blank rows are the spacers the crawl is
///         built from. What is left worth reporting is whether the batch composes at all: an index
///         that no longer exists, or a command the format cannot represent.
///     </para>
/// </summary>
public sealed class ValidateCreditsBatchHandler
    : IJsonRpcRequestHandler<ValidateCreditsBatchParams, ValidateCreditsBatchResult>
{
    private readonly ILspConfigurationProvider _config;
    private readonly ILocalisationDocumentEditor _editor;
    private readonly IFileHelper _fileHelper;

    public ValidateCreditsBatchHandler(
        ILocalisationDocumentEditor editor,
        IFileHelper fileHelper,
        ILspConfigurationProvider config)
    {
        _editor = editor;
        _fileHelper = fileHelper;
        _config = config;
    }

    public Task<ValidateCreditsBatchResult> Handle(
        ValidateCreditsBatchParams request, CancellationToken ct)
    {
        if (!_config.Current.Features.Tools.Localisation)
            return Task.FromResult(
                new ValidateCreditsBatchResult([], LocalisationFeatureDisabled.Message));

        var fs = _fileHelper.FileSystem;
        if (string.IsNullOrWhiteSpace(request.ProjectFilePath)
            || !fs.File.Exists(request.ProjectFilePath))
            return Task.FromResult(new ValidateCreditsBatchResult(
                [], $"File not found: {request.ProjectFilePath}"));

        LocalisationEditResult composed;
        try
        {
            composed = _editor.DryRunFile(request.ProjectFilePath, request.Commands);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ValidateCreditsBatchResult(
                [], $"Failed to read the file: {ex.Message}"));
        }

        if (!composed.Success)
            return Task.FromResult(new ValidateCreditsBatchResult(
            [
                new LocProblemDto(composed.FailedIndex, null, LocProblemSeverity.Error,
                    $"Change {(composed.FailedIndex ?? 0) + 1}: {composed.Error}")
            ]));

        // Nothing about a credits key is checkable - duplicates and blanks are the format. Nor is
        // "this language has fewer entries": a German dub recorded with a smaller cast is a shorter
        // list, not a broken one, and the export dropping the empty entries is what lets one file
        // carry both.
        //
        // A heading with nothing under it is reported, but only as information. A credits key is a
        // formatting directive, so a HEADER with no CENTER lines beneath it is a valid file that
        // renders exactly as written - it is usually an unfinished translation, which is worth
        // seeing, but calling it a problem overstates a file the engine reads without complaint.
        var problems = new List<LocProblemDto>();
        var (rows, languages, rowsError) =
            _editor.DryRunRows(request.ProjectFilePath, request.Commands);
        if (rowsError is null)
            foreach (var (language, index, label) in
                     CreditsCoverageInspector.Inspect(rows, languages))
                problems.Add(new LocProblemDto(index, language, LocProblemSeverity.Info,
                    CreditsCoverageInspector.Message(language, label)));

        return Task.FromResult(new ValidateCreditsBatchResult(problems));
    }
}
