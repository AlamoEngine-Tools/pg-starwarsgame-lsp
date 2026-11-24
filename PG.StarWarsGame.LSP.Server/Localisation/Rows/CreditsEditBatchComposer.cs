// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Localisation;
using PG.StarWarsGame.LSP.Localisation.Rows;
using PG.StarWarsGame.LSP.Server.Editing;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     The credits editor's side of the write-back contract. Its subject is the one credits file
///     it shows; its commands are <see cref="LocEditCommandDto" />s, addressed by row, because a
///     credits file is ordered and repeats its keys.
/// </summary>
public sealed class CreditsEditBatchComposer(
    LocalisationBatchDocuments documents,
    ILocalisationDocumentEditor editor,
    ILogger<CreditsEditBatchComposer> logger,
    IMessageCatalog messages) : IEditBatchComposer
{
    /// <summary>The <c>Editor</c> value of the credits editor's batches.</summary>
    public const string EditorName = "credits";

    public string Editor => EditorName;

    public Task<ValidateEditBatchResult> ValidateAsync(
        EditBatchSubject subject, JArray commands, CancellationToken ct)
    {
        if (EditBatchCommands.Read<LocEditCommandDto>(commands, messages, out var staged) is { } unreadable)
            return Task.FromResult(new ValidateEditBatchResult([], unreadable));

        if (documents.First(subject, "LocValidateCredits_FileNotFound", out var filePath) is { } notFound)
            return Task.FromResult(new ValidateEditBatchResult([], notFound));

        LocalisationEditResult composed;
        try
        {
            composed = editor.DryRunFile(filePath, staged);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ValidateEditBatchResult(
                [], messages.Format("LocValidateCredits_FailedReadFile", ex.Message)));
        }

        // An addressing failure is about one staged change, not about a row, so it is reported
        // against the batch with the command number the user can count to in the queue - and with
        // no Index. The two indices are unrelated: LocalisationEditResult.FailedIndex counts
        // commands in the batch, a problem's Index is a row in the file. The grid tints the row
        // Index names and scrolls to it, so passing the command number here highlighted whichever
        // row happened to share its number. Same shape as the translation composer.
        if (!composed.Success)
            return Task.FromResult(new ValidateEditBatchResult(
            [
                new EditBatchProblemDto(LocProblemSeverity.Error,
                    messages.Format("LocValidateCredits_Change",
                        (composed.FailedIndex ?? 0) + 1, composed.Error))
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
        var problems = new List<EditBatchProblemDto>();
        var (rows, languages, rowsError) = editor.DryRunRows(filePath, staged);
        if (rowsError is null)
            foreach (var (language, index, label) in CreditsCoverageInspector.Inspect(rows, languages))
                problems.Add(new EditBatchProblemDto(LocProblemSeverity.Info,
                    CreditsCoverageInspector.Message(language, label, messages), Language: language,
                    Index: index));

        return Task.FromResult(new ValidateEditBatchResult(problems));
    }

    public Task<ApplyEditBatchResult> ComposeAsync(
        EditBatchSubject subject, JArray commands, CancellationToken ct)
    {
        if (EditBatchCommands.Read<LocEditCommandDto>(commands, messages, out var staged) is { } unreadable)
            return Task.FromResult(new ApplyEditBatchResult(false, Error: unreadable));

        if (documents.Open(subject, out var files) is { } refused)
            return Task.FromResult(new ApplyEditBatchResult(false, Error: refused));

        // A credits file is one file: its rows are addressed by position, which no second file shares.
        var file = files[0];
        if (staged.Count == 0)
            return Task.FromResult(new ApplyEditBatchResult(true,
                Documents: LocalisationBatchDocuments.Written([file], [file.Text])));

        LocalisationEditResult composed;
        try
        {
            composed = editor.Apply(file.Text, file.Extension, staged, file.FilePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not compose the credits batch for '{Path}'.", file.FilePath);
            return Task.FromResult(new ApplyEditBatchResult(false,
                Error: messages.Format("LocApplyBatch_CouldNotWriteFile", ex.Message)));
        }

        if (!composed.Success)
            return Task.FromResult(new ApplyEditBatchResult(false, composed.FailedIndex, composed.Error));

        return Task.FromResult(new ApplyEditBatchResult(true,
            Edit: LocalisationBatchDocuments.Edit([file], [composed.NewText!]),
            Documents: LocalisationBatchDocuments.Written([file], [composed.NewText!])));
    }
}