// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Commits a staged batch of translation edits. All or nothing: if any command fails the file is
///     left untouched and the client keeps its queue, so a partial save can never leave the grid and
///     the file disagreeing about what was written.
///     <para>
///         Separate from the credits batch because the two file kinds are addressed differently -
///         translations by key, credits by position - and one endpoint serving both is what made a
///         single grid branch on the file kind in dozens of places.
///     </para>
/// </summary>
[Method("aet/applyTranslationBatch", Direction.ClientToServer)]
public sealed record ApplyTranslationBatchParams(
    string ProjectFilePath,
    string? ExpectedContentHash,
    IReadOnlyList<LocKeyedCommandDto> Commands) : IRequest<ApplyTranslationBatchResult>;

/// <param name="FailedIndex">0-based position of the command that failed, for pointing the user at it.</param>
/// <param name="NewContentHash">
///     Hash of the text just written; the client must echo it on the next save for this file.
/// </param>
public sealed record ApplyTranslationBatchResult(
    bool Success,
    int? FailedIndex = null,
    string? Error = null,
    string? NewContentHash = null);

/// <summary>
///     Dry-runs a batch of translation edits and reports what is wrong, without writing.
/// </summary>
[Method("aet/validateTranslationBatch", Direction.ClientToServer)]
public sealed record ValidateTranslationBatchParams(
    string ProjectFilePath,
    IReadOnlyList<LocKeyedCommandDto> Commands) : IRequest<ValidateTranslationBatchResult>;

/// <summary>
///     A problem with a translation entry.
/// </summary>
/// <param name="Key">
///     The entry the problem sits on, or null when it is about the batch as a whole. A key rather
///     than a row number because a translation file has no row order the user can see.
/// </param>
/// <param name="Severity"><c>error</c> or <c>warning</c>.</param>
/// <param name="Index">
///     The row the problem sits on, so the grid can highlight it. Needed as well as
///     <paramref name="Key" /> because the one problem a key cannot identify is a row that has no
///     key - and a blank key is precisely the row worth pointing at.
/// </param>
public sealed record LocTranslationProblemDto(
    string? Key, string? Language, string Severity, string Message, int? Index = null);

public sealed record ValidateTranslationBatchResult(
    IReadOnlyList<LocTranslationProblemDto> Problems,
    string? Error = null);
