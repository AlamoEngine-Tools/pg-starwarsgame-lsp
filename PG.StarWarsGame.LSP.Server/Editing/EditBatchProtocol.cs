// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using JetBrains.Annotations;
using MediatR;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Server.Editing;

// ── aet/validateEditBatch, aet/applyEditBatch - the visual editors' one way to write ─────────────
//
// A visual editor stages its changes and sends them as one batch: to be checked, or to be written.
// The envelope is the same for every editor; what differs is the commands it carries, which each
// editor's composer reads as its own.

/// <summary>
///     What a batch is about, as the editor knows it. The server resolves it to files; an editor
///     never names a path.
/// </summary>
/// <param name="Campaign">The story editor's campaign.</param>
/// <param name="Faction">The story editor's faction within it.</param>
/// <param name="Documents">
///     The localisation editors' file, or the set of single-language files the grid shows as one
///     table. Which file of a set a command goes to is the server's decision.
/// </param>
[PublicAPI]
public sealed record EditBatchSubject(
    string? Campaign = null,
    string? Faction = null,
    IReadOnlyList<EditBatchDocument>? Documents = null);

/// <summary>One localisation file an editor shows, as the server listed it.</summary>
/// <param name="ContentHash">
///     In a request, the hash of the text the editor last read: a batch is staged against that
///     text and is refused for a file that has changed since. In an answer, the hash the file has
///     once the batch is written, which the editor's next batch has to carry.
/// </param>
[PublicAPI]
public sealed record EditBatchDocument(string FilePath, string? ContentHash = null);

/// <summary>
///     Checks a staged batch without writing: the commands are applied to a working copy and the
///     problems of that pending state come back.
/// </summary>
/// <param name="Editor">Which editor sends the batch; it selects the composer that reads the commands.</param>
/// <param name="Subject">What the batch is about.</param>
/// <param name="Commands">The staged commands in the order they were made, in the editor's own shape.</param>
[Method("aet/validateEditBatch", Direction.ClientToServer)]
public sealed record ValidateEditBatchParams(string Editor, EditBatchSubject Subject, JArray Commands)
    : IRequest<ValidateEditBatchResult>;

/// <summary>
///     One problem of the pending state. A flat union: each editor fills the anchors it has.
/// </summary>
/// <param name="Severity"><c>error</c>, <c>warning</c> or <c>info</c>.</param>
/// <param name="NodeId">Story: the graph node the problem belongs to; null for a problem of the file.</param>
/// <param name="Side">Story: <c>event</c> or <c>reward</c> when the problem lies in a parameter slot.</param>
/// <param name="Position">Story: the 0-based parameter slot, when <paramref name="Side" /> is set.</param>
/// <param name="Uri">The file the problem is in.</param>
/// <param name="Line">0-based line in <paramref name="Uri" />.</param>
/// <param name="Column">0-based column in <paramref name="Line" />.</param>
/// <param name="Key">Localisation: the entry the problem is about; null for a problem of the whole file.</param>
/// <param name="Language">Localisation: the language the problem is about.</param>
/// <param name="Index">Localisation: the row the problem is about, 0-based in the file.</param>
[PublicAPI]
public sealed record EditBatchProblemDto(
    string Severity,
    string Message,
    string? NodeId = null,
    string? Side = null,
    int? Position = null,
    string? Uri = null,
    int? Line = null,
    int? Column = null,
    string? Key = null,
    string? Language = null,
    int? Index = null);

/// <param name="Error">Why the batch could not be checked at all; the problems are then empty.</param>
[PublicAPI]
public sealed record ValidateEditBatchResult(IReadOnlyList<EditBatchProblemDto> Problems, string? Error = null);

/// <summary>
///     Composes a staged batch into the edit that writes it. The commands are applied in order to
///     one working copy, so each sees the result of those before it; the first that fails stops the
///     batch and nothing is composed.
///     <para>
///         The edit comes back in the answer and the client applies it. The server cannot apply it
///         inside this request: the client reports the change before it answers an edit the server
///         sends, and that report cancels every request still running - this one included.
///     </para>
/// </summary>
/// <param name="Editor">Which editor sends the batch; it selects the composer that reads the commands.</param>
/// <param name="Subject">What the batch is about.</param>
/// <param name="Commands">The staged commands in the order they were made, in the editor's own shape.</param>
[Method("aet/applyEditBatch", Direction.ClientToServer)]
public sealed record ApplyEditBatchParams(string Editor, EditBatchSubject Subject, JArray Commands)
    : IRequest<ApplyEditBatchResult>;

/// <param name="FailedIndex">0-based index of the command that failed, when <c>Success</c> is false.</param>
/// <param name="Edit">
///     Every file the batch changes, as one edit for the client to apply. Null when the batch
///     failed, and when it changes nothing.
/// </param>
/// <param name="Documents">Localisation: each file of the subject with the content hash it has once the edit is written.</param>
[PublicAPI]
public sealed record ApplyEditBatchResult(
    bool Success,
    int? FailedIndex = null,
    string? Error = null,
    WorkspaceEdit? Edit = null,
    IReadOnlyList<EditBatchDocument>? Documents = null);