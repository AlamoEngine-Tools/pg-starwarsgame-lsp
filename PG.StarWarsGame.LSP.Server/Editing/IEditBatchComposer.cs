// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Newtonsoft.Json.Linq;

namespace PG.StarWarsGame.LSP.Server.Editing;

/// <summary>
///     One visual editor's side of the write-back contract: it reads the batch's commands as its
///     own, resolves the subject to files, and composes the edit.
///     <para>
///         A composer writes nothing. It returns the edit, and an editor that models part of a file
///         leaves the rest of it as it was.
///     </para>
/// </summary>
public interface IEditBatchComposer
{
    /// <summary>The <c>Editor</c> value of the batches this composer reads.</summary>
    string Editor { get; }

    /// <summary>The problems of the state the batch would leave, without composing an edit.</summary>
    Task<ValidateEditBatchResult> ValidateAsync(EditBatchSubject subject, JArray commands, CancellationToken ct);

    /// <summary>The edit that writes the batch, or the command that stopped it.</summary>
    Task<ApplyEditBatchResult> ComposeAsync(EditBatchSubject subject, JArray commands, CancellationToken ct);
}