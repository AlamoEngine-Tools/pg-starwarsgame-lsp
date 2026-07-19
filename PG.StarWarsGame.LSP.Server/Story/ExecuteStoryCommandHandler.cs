// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     Executes a single story editor mutation: validates against the campaign model via
///     <see cref="StoryCommandExecutor" />, then applies the resulting edits through
///     <c>workspace/applyEdit</c> (undo/redo and open-editor sync come free). Non-project layers
///     are read-only — mutations there are rejected with the override-copy guidance. Batched
///     edit-mode saves go through <see cref="ApplyStoryCommandBatchHandler" />, which composes many
///     commands over one <see cref="WorkingTextSet" /> before a single applyEdit.
/// </summary>
public sealed class ExecuteStoryCommandHandler(
    IStoryModelService modelService,
    IGameIndexService indexService,
    IDocumentTextSource textSource,
    ISchemaProvider schema,
    IFileHelper fileHelper,
    IModProjectReloadService reloadService,
    IWorkspaceEditApplier applier,
    ILspConfigurationProvider config,
    ILogger<ExecuteStoryCommandHandler> logger)
    : IJsonRpcRequestHandler<ExecuteStoryCommandParams, ExecuteStoryCommandResult>
{
    public Task<ExecuteStoryCommandResult> Handle(ExecuteStoryCommandParams request, CancellationToken ct)
    {
        if (StoryEditingFeature.Rejection(config) is { } rejection)
            return Task.FromResult(Error(rejection));

        var model = modelService.GetCampaignModel(request.Campaign);
        if (model is null)
            return Task.FromResult(Error($"Campaign '{request.Campaign}' was not found."));

        var executor = new StoryCommandExecutor(
            modelService, indexService, textSource, schema, fileHelper, reloadService, logger);
        var (produced, error) = executor.Produce(request, model, new WorkingTextSet(textSource));
        if (error is not null)
            return Task.FromResult(Error(error));

        logger.LogDebug("Story command {Kind} → applyEdit", request.Kind);
        return Task.FromResult(ApplyDetached(
            StoryCommandExecutor.BuildWorkspaceEdit(produced!), produced!.Label));
    }

    /// <summary>
    ///     Sends <c>workspace/applyEdit</c> without awaiting it inside this request. The client
    ///     applying the edit sends <c>textDocument/didChange</c> back BEFORE answering, and
    ///     OmniSharp (content-modified support is on by default) cancels every in-flight request
    ///     with <c>ContentModified</c> when content changes — awaiting the round-trip here would
    ///     kill our own response with the client's "Content Modified" error. Rejections are rare
    ///     (the client auto-applies well-formed edits) and get logged instead of surfaced.
    /// </summary>
    private ExecuteStoryCommandResult ApplyDetached(WorkspaceEdit edit, string label)
    {
        _ = SendAsync();
        return new ExecuteStoryCommandResult(true);

        async Task SendAsync()
        {
            try
            {
                if (!await applier.ApplyAsync(edit, label, CancellationToken.None))
                    logger.LogWarning("workspace/applyEdit was rejected: {Label} | {Edit}",
                        label, DescribeEdit(edit));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "workspace/applyEdit failed: {Label}", label);
            }
        }
    }

    /// <summary>One-line dump of a WorkspaceEdit (uris, ranges, new text) for diagnosing rejections.</summary>
    private static string DescribeEdit(WorkspaceEdit edit)
    {
        static string One(string uri, IEnumerable<TextEdit> edits)
        {
            return uri + " => " + string.Join(", ", edits.Select(e =>
                $"[{e.Range.Start.Line},{e.Range.Start.Character}-{e.Range.End.Line},{e.Range.End.Character}]='{e.NewText}'"));
        }

        if (edit.Changes is { } changes)
            return "changes{ " + string.Join(" ; ", changes.Select(kvp => One(kvp.Key.ToString(), kvp.Value))) + " }";
        if (edit.DocumentChanges is { } docChanges)
            return "documentChanges{ " + string.Join(" ; ", docChanges
                .Where(c => c.IsTextDocumentEdit)
                .Select(c => One(c.TextDocumentEdit!.TextDocument.Uri.ToString(), c.TextDocumentEdit!.Edits))) + " }";
        return "(empty)";
    }

    private static ExecuteStoryCommandResult Error(string message)
    {
        return new ExecuteStoryCommandResult(false, message);
    }
}