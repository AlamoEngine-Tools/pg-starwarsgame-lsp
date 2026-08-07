// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;

using PG.Commons.Hashing;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Dry-runs a staged batch of translation edits and reports what is wrong, without writing.
///     <para>
///         Resolves the keys and inspects the key list the batch would leave behind, rather than
///         composing the whole file and parsing it back. That is both cheaper on a 19,000-row file
///         and the only way a compiled <c>.dat</c> can be validated at all, since it has no text to
///         compose.
///     </para>
/// </summary>
public sealed class ValidateTranslationBatchHandler
    : IJsonRpcRequestHandler<ValidateTranslationBatchParams, ValidateTranslationBatchResult>
{
    private readonly ILspConfigurationProvider _config;
    private readonly ILocalisationDocumentEditor _editor;
    private readonly IFileHelper _fileHelper;
    private readonly ICrc32HashingService _hashing;

    public ValidateTranslationBatchHandler(
        ILocalisationDocumentEditor editor,
        IFileHelper fileHelper,
        ICrc32HashingService hashing,
        ILspConfigurationProvider config)
    {
        _editor = editor;
        _fileHelper = fileHelper;
        _hashing = hashing;
        _config = config;
    }

    public Task<ValidateTranslationBatchResult> Handle(
        ValidateTranslationBatchParams request, CancellationToken ct)
    {
        if (!_config.Current.Features.Tools.Localisation)
            return Task.FromResult(
                new ValidateTranslationBatchResult([], LocalisationFeatureDisabled.Message));

        var fs = _fileHelper.FileSystem;
        if (string.IsNullOrWhiteSpace(request.ProjectFilePath)
            || !fs.File.Exists(request.ProjectFilePath))
            return Task.FromResult(new ValidateTranslationBatchResult(
                [], $"File not found: {request.ProjectFilePath}"));

        KeyedTranslationResult translated;
        try
        {
            translated = _editor.TranslateKeyed(request.ProjectFilePath, request.Commands);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ValidateTranslationBatchResult(
                [], $"Failed to read the file: {ex.Message}"));
        }

        // An addressing failure is about one staged change, not about an entry, so it is reported
        // against the batch with the command number the user can count to in the queue.
        if (!translated.Success)
            return Task.FromResult(new ValidateTranslationBatchResult(
            [
                new LocTranslationProblemDto(null, null, LocProblemSeverity.Error,
                    $"Change {(translated.FailedIndex ?? 0) + 1}: {translated.Error}")
            ]));

        var problems = TranslationKeyInspector.Inspect(translated.ResultingKeys ?? [], _hashing).ToList();

        // A language left half-filled is not a key problem, so it is checked from the resulting
        // rows rather than from the key list.
        var (rows, languages, rowsError) =
            _editor.DryRunRows(request.ProjectFilePath, translated.Commands ?? []);
        if (rowsError is null)
            foreach (var (language, empty, total) in
                     LanguageCoverageInspector.Inspect(rows, languages))
                problems.Add(new LocTranslationProblemDto(null, language, LocProblemSeverity.Warning,
                    LanguageCoverageInspector.Message(language, empty, total)));

        return Task.FromResult(new ValidateTranslationBatchResult(problems));
    }
}
