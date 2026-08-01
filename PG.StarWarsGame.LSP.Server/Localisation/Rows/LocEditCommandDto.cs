// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     One staged edit. Rows are addressed by index, resolved against the document as of all
///     preceding commands in the same batch, so a batch composes the way the user performed it.
/// </summary>
/// <param name="Kind">
///     <c>setCell</c>, <c>setKey</c>, <c>insertRow</c>, <c>deleteRow</c>, <c>moveRow</c> or
///     <c>addLanguage</c>.
/// </param>
/// <param name="Index">Target row. Absent only for <c>addLanguage</c>.</param>
/// <param name="ToIndex">Destination row for <c>moveRow</c>.</param>
/// <param name="Key">New key for <c>setKey</c>, or the key of an inserted row.</param>
/// <param name="Language">Column for <c>setCell</c>, or the language being added.</param>
/// <param name="Value">New cell value for <c>setCell</c>.</param>
/// <param name="Values">Cell values for an inserted row.</param>
/// <param name="ExpectedKey">
///     The key the client believes is at <paramref name="Index" />. Optional, but when given a
///     mismatch fails the batch: if the two views of row order have drifted, an index-addressed
///     edit silently lands on the wrong row, and a clear error beats a corrupted file.
/// </param>
public sealed record LocEditCommandDto(
    string Kind,
    int? Index = null,
    int? ToIndex = null,
    string? Key = null,
    string? Language = null,
    string? Value = null,
    IReadOnlyList<LocValueDto>? Values = null,
    string? ExpectedKey = null);

/// <summary>
///     The outcome of composing a batch.
/// </summary>
/// <param name="Success">Whether every command applied.</param>
/// <param name="NewText">The file's new contents, or null when the batch failed.</param>
/// <param name="FailedIndex">0-based position of the offending command in the batch.</param>
/// <param name="Error">What went wrong, phrased for the user.</param>
public sealed record LocalisationEditResult(
    bool Success,
    string? NewText = null,
    int? FailedIndex = null,
    string? Error = null)
{
    public static LocalisationEditResult Ok(string newText)
    {
        return new LocalisationEditResult(true, newText);
    }

    public static LocalisationEditResult Fail(int commandIndex, string error)
    {
        return new LocalisationEditResult(false, null, commandIndex, error);
    }
}
