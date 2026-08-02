// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server.Localisation.Rows;

/// <summary>
///     Commits a staged batch of credits edits. All or nothing, like its translation counterpart.
///     <para>
///         Credits stay addressed by position: the file is a running order the crawl plays top to
///         bottom, the same key repeats hundreds of times as a formatting directive, and blank rows
///         are content. None of that can be named by key.
///     </para>
/// </summary>
[Method("aet/applyCreditsBatch", Direction.ClientToServer)]
public sealed record ApplyCreditsBatchParams(
    string ProjectFilePath,
    string? ExpectedContentHash,
    IReadOnlyList<LocEditCommandDto> Commands) : IRequest<ApplyCreditsBatchResult>;

/// <param name="FailedIndex">0-based position of the command that failed, for pointing the user at it.</param>
/// <param name="NewContentHash">
///     Hash of the text just written; the client must echo it on the next save for this file.
/// </param>
public sealed record ApplyCreditsBatchResult(
    bool Success,
    int? FailedIndex = null,
    string? Error = null,
    string? NewContentHash = null);

/// <summary>
///     Dry-runs a batch of credits edits without writing.
///     <para>
///         There are no key rules to check here - duplicate keys and blank rows are the format - so
///         what this reports is whether the batch composes at all.
///     </para>
/// </summary>
[Method("aet/validateCreditsBatch", Direction.ClientToServer)]
public sealed record ValidateCreditsBatchParams(
    string ProjectFilePath,
    IReadOnlyList<LocEditCommandDto> Commands) : IRequest<ValidateCreditsBatchResult>;

/// <param name="Index">Row the problem sits on, or null when it is about the batch as a whole.</param>
/// <param name="Severity"><c>error</c> or <c>warning</c>.</param>
public sealed record LocProblemDto(int? Index, string? Language, string Severity, string Message);

public sealed record ValidateCreditsBatchResult(
    IReadOnlyList<LocProblemDto> Problems,
    string? Error = null);
