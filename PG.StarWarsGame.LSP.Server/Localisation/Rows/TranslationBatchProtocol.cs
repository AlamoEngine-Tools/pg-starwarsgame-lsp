// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using JetBrains.Annotations;
using MediatR;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Localisation.Rows;

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

public sealed record ValidateTranslationBatchResult(
    IReadOnlyList<LocTranslationProblemDto> Problems,
    string? Error = null);