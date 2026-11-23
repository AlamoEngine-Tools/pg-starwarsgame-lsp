// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Messages;

namespace PG.StarWarsGame.LSP.Server.Editing;

/// <summary>The composers by the editor they serve.</summary>
public sealed class EditBatchComposers(IEnumerable<IEditBatchComposer> composers)
{
    private readonly Dictionary<string, IEditBatchComposer> _byEditor =
        composers.ToDictionary(c => c.Editor, StringComparer.Ordinal);

    /// <summary>The composer for <paramref name="editor" />, or null when no editor of that name writes here.</summary>
    public IEditBatchComposer? For(string? editor)
    {
        return editor is not null && _byEditor.TryGetValue(editor, out var composer) ? composer : null;
    }
}

/// <summary><c>aet/validateEditBatch</c>: hands the batch to its editor's composer to be checked.</summary>
public sealed class ValidateEditBatchHandler(EditBatchComposers composers, IMessageCatalog messages)
    : IJsonRpcRequestHandler<ValidateEditBatchParams, ValidateEditBatchResult>
{
    public Task<ValidateEditBatchResult> Handle(ValidateEditBatchParams request, CancellationToken ct)
    {
        return composers.For(request.Editor) is { } composer
            ? composer.ValidateAsync(request.Subject, request.Commands, ct)
            : Task.FromResult(new ValidateEditBatchResult([],
                messages.Format("EditBatch_UnknownEditor", request.Editor)));
    }
}

/// <summary><c>aet/applyEditBatch</c>: hands the batch to its editor's composer to be composed.</summary>
public sealed class ApplyEditBatchHandler(EditBatchComposers composers, IMessageCatalog messages)
    : IJsonRpcRequestHandler<ApplyEditBatchParams, ApplyEditBatchResult>
{
    public Task<ApplyEditBatchResult> Handle(ApplyEditBatchParams request, CancellationToken ct)
    {
        return composers.For(request.Editor) is { } composer
            ? composer.ComposeAsync(request.Subject, request.Commands, ct)
            : Task.FromResult(new ApplyEditBatchResult(false,
                Error: messages.Format("EditBatch_UnknownEditor", request.Editor)));
    }
}