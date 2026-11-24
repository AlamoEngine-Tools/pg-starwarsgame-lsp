// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using PG.Commons.Hashing;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Localisation;
using PG.StarWarsGame.LSP.Localisation.Rows;
using PG.StarWarsGame.LSP.Server.Editing;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     The localisation grid's side of the write-back contract. Its subject is the file the grid
///     shows, or the set of single-language files it shows as one table; its commands are
///     <see cref="LocKeyedCommandDto" />s, addressed by key and language.
///     <para>
///         Which file of a set a command goes to is decided here. The files are composed from
///         their texts and come back as one edit, so a batch across a set is written whole or not
///         at all.
///     </para>
/// </summary>
public sealed class TranslationEditBatchComposer(
    LocalisationBatchDocuments documents,
    ILocalisationDocumentEditor editor,
    ILocalisationRowReader reader,
    ICrc32HashingService hashing,
    ILogger<TranslationEditBatchComposer> logger,
    IMessageCatalog messages) : IEditBatchComposer
{
    /// <summary>The <c>Editor</c> value of the localisation grid's batches.</summary>
    public const string EditorName = "translation";

    public string Editor => EditorName;

    public Task<ValidateEditBatchResult> ValidateAsync(
        EditBatchSubject subject, JArray commands, CancellationToken ct)
    {
        if (EditBatchCommands.Read<LocKeyedCommandDto>(commands, messages, out var staged) is { } unreadable)
            return Task.FromResult(new ValidateEditBatchResult([], unreadable));

        if (documents.First(subject, "LocValidateTranslation_FileNotFound", out var filePath) is { } notFound)
            return Task.FromResult(new ValidateEditBatchResult([], notFound));

        KeyedTranslationResult translated;
        try
        {
            translated = editor.TranslateKeyed(filePath, staged);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ValidateEditBatchResult(
                [], messages.Format("LocValidateTranslation_FailedReadFile", ex.Message)));
        }

        // An addressing failure is about one staged change, not about an entry, so it is reported
        // against the batch with the command number the user can count to in the queue.
        if (!translated.Success)
            return Task.FromResult(new ValidateEditBatchResult(
            [
                new EditBatchProblemDto(LocProblemSeverity.Error,
                    messages.Format("LocValidateTranslation_Change",
                        (translated.FailedIndex ?? 0) + 1, translated.Error))
            ]));

        var problems = TranslationKeyInspector.Inspect(translated.ResultingKeys ?? [], hashing, messages)
            .Select(p => new EditBatchProblemDto(p.Severity, p.Message, Key: p.Key, Language: p.Language,
                Index: p.Index))
            .ToList();

        // A language left half-filled is not a key problem, so it is checked from the resulting
        // rows rather than from the key list.
        var (rows, languages, rowsError) = editor.DryRunRows(filePath, translated.Commands ?? []);
        if (rowsError is null)
            foreach (var (language, empty, total) in LanguageCoverageInspector.Inspect(rows, languages))
                problems.Add(new EditBatchProblemDto(LocProblemSeverity.Warning,
                    LanguageCoverageInspector.Message(language, empty, total, messages), Language: language));

        return Task.FromResult(new ValidateEditBatchResult(problems));
    }

    public Task<ApplyEditBatchResult> ComposeAsync(
        EditBatchSubject subject, JArray commands, CancellationToken ct)
    {
        if (EditBatchCommands.Read<LocKeyedCommandDto>(commands, messages, out var staged) is { } unreadable)
            return Task.FromResult(new ApplyEditBatchResult(false, Error: unreadable));

        if (documents.Open(subject, out var files) is { } refused)
            return Task.FromResult(new ApplyEditBatchResult(false, Error: refused));

        var texts = files.Select(f => f.Text).ToList();
        if (staged.Count == 0)
            return Task.FromResult(new ApplyEditBatchResult(true,
                Documents: LocalisationBatchDocuments.Written(files, texts)));

        try
        {
            var read = files.Select(f => reader.Read(f.Text, f.Extension, f.FilePath)).ToList();
            var parts = TranslationSetPartition.Split(staged, read.Select(d => d.Languages).ToList());

            for (var member = 0; member < files.Count; member++)
            {
                // Untouched by this batch: its text stays as it is and it is not in the edit.
                if (parts[member].Count == 0) continue;

                var translated = KeyedCommandTranslator.Translate(
                    read[member], parts[member].Select(p => p.Command).ToList(), hashing, messages);
                if (!translated.Success)
                    return Task.FromResult(new ApplyEditBatchResult(false,
                        translated.FailedIndex is { } failed ? parts[member][failed].Index : null,
                        translated.Error));

                var composed = editor.Apply(
                    files[member].Text, files[member].Extension, translated.Commands ?? [], files[member].FilePath);
                if (!composed.Success)
                    return Task.FromResult(new ApplyEditBatchResult(false, Error: composed.Error));

                texts[member] = composed.NewText!;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not compose the translation batch for '{Path}'.", files[0].FilePath);
            return Task.FromResult(new ApplyEditBatchResult(false,
                Error: messages.Format("LocApplyBatch_CouldNotWriteFile", ex.Message)));
        }

        return Task.FromResult(new ApplyEditBatchResult(true,
            Edit: LocalisationBatchDocuments.Edit(files, texts),
            Documents: LocalisationBatchDocuments.Written(files, texts)));
    }
}