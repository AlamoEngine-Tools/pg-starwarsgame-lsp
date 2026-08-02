// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Composes a staged batch into the file's new text.
///     <para>
///         String in, string out, with no file access, so the whole save path can be tested
///         exhaustively without touching a disk - and so the caller can run a batch as a dry run
///         simply by discarding the result.
///     </para>
///     <para>
///         <b>Round-trip contract:</b> a row the batch does not touch is written back exactly as it
///         was read, byte for byte, including its original quoting. Only edited rows are
///         re-serialised, and the header, comments, blank lines and line endings are preserved. A
///         save that reformats untouched rows would produce an unreviewable diff on a large file.
///     </para>
/// </summary>
public interface ILocalisationDocumentEditor
{
    LocalisationEditResult Apply(
        string originalText, string extension, IReadOnlyList<LocEditCommandDto> commands);

    /// <summary>
    ///     Applies a batch to a file on disk and writes the result.
    ///     <para>
    ///         Owns the write because <c>.dat</c> is binary: there is no text to hand back for a
    ///         caller to save, and the DAT services write through a real file stream. Text formats
    ///         still compose through <see cref="Apply" /> and keep their round-trip guarantee.
    ///     </para>
    /// </summary>
    Task<LocalisationEditResult> ApplyToFileAsync(
        string filePath, IReadOnlyList<LocEditCommandDto> commands, CancellationToken ct);

    /// <summary>
    ///     Applies a batch of key-addressed translation edits, resolving each key to a row before
    ///     handing the result to <see cref="ApplyToFileAsync" />.
    ///     <para>
    ///         The resolution is <see cref="KeyedCommandTranslator" />; this is only the seam that
    ///         gives it the document. Positions therefore exist in exactly one place, and callers
    ///         above never see one.
    ///     </para>
    /// </summary>
    Task<LocalisationEditResult> ApplyKeyedToFileAsync(
        string filePath, IReadOnlyList<LocKeyedCommandDto> commands, CancellationToken ct);

    /// <summary>
    ///     Resolves key-addressed commands against the file without applying them, so a dry run can
    ///     report addressing failures and inspect the keys the batch would leave behind.
    /// </summary>
    KeyedTranslationResult TranslateKeyed(
        string filePath, IReadOnlyList<LocKeyedCommandDto> commands);

    /// <summary>
    ///     Runs a batch against the file and throws the result away, reporting only whether it
    ///     composes.
    ///     <para>
    ///         Reads from the path rather than taking text, so a compiled <c>.dat</c> can be dry-run
    ///         at all - credits ship as <c>.dat</c>, and handing one in as a string is not possible.
    ///     </para>
    /// </summary>
    LocalisationEditResult DryRunFile(string filePath, IReadOnlyList<LocEditCommandDto> commands);

    /// <summary>
    ///     The rows and languages the file would hold once a batch lands, without writing.
    /// </summary>
    /// <remarks>
    ///     <see cref="DryRunFile" /> answers "does this compose"; this answers "what would it say",
    ///     which is what any check about the <em>content</em> needs - language coverage, to begin
    ///     with. Returns no rows and the reason when the batch does not compose.
    /// </remarks>
    (IReadOnlyList<LocRowDto> Rows, IReadOnlyList<string> Languages, string? Error) DryRunRows(
        string filePath, IReadOnlyList<LocEditCommandDto> commands);
}
