// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Xml.Util;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     Commits a staged edit-mode batch. Drives the commands in order over one
///     <see cref="WorkingTextSet" />, re-parsing each step so a command sees the results of the
///     commands before it. The first failure aborts the batch: nothing is written and the failing
///     command's index is returned. On full success the composed working texts are written as a
///     single <c>workspace/applyEdit</c> (one whole-document replacement per changed file, plus a
///     create for any new thread/manifest) - undo/redo and open-editor sync come free, and the
///     following <c>aet/storyGraphChanged</c> reconciles the client to committed truth.
/// </summary>
public sealed class ApplyStoryCommandBatchHandler(
    IStoryModelService modelService,
    IGameIndexService indexService,
    IDocumentTextSource textSource,
    ISchemaProvider schema,
    IFileHelper fileHelper,
    IModProjectReloadService reloadService,
    IWorkspaceEditApplier applier,
    ILspConfigurationProvider config,
    ILogger<ApplyStoryCommandBatchHandler> logger)
    : IJsonRpcRequestHandler<ApplyStoryCommandBatchParams, ApplyStoryCommandBatchResult>
{
    public Task<ApplyStoryCommandBatchResult> Handle(
        ApplyStoryCommandBatchParams request, CancellationToken ct)
    {
        if (StoryEditingFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new ApplyStoryCommandBatchResult(false, Error: rejection));

        var model = modelService.GetCampaignModel(request.Campaign);
        if (model is null)
            return Task.FromResult(new ApplyStoryCommandBatchResult(false,
                Error: $"Campaign '{request.Campaign}' was not found."));

        if (request.Commands.Count == 0)
            return Task.FromResult(new ApplyStoryCommandBatchResult(true));

        var executor = new StoryCommandExecutor(
            modelService, indexService, textSource, schema, fileHelper, reloadService, logger);
        var texts = new WorkingTextSet(textSource);

        var (failedIndex, error) = executor.Compose(
            model, request.Commands.Select(c => c.ToParams(request.Campaign)).ToList(), texts);
        if (error is not null)
            return Task.FromResult(new ApplyStoryCommandBatchResult(false, failedIndex, error));

        var changed = texts.Changed().ToList();
        if (changed.Count == 0)
            return Task.FromResult(new ApplyStoryCommandBatchResult(true));

        var label = $"Apply {request.Commands.Count} story change(s)";
        logger.LogDebug("Story batch of {Count} → applyEdit touching {Files} file(s)",
            request.Commands.Count, changed.Count);
        _ = SendAsync(BuildBatchEdit(changed), label);
        return Task.FromResult(new ApplyStoryCommandBatchResult(true));

        async Task SendAsync(WorkspaceEdit edit, string editLabel)
        {
            try
            {
                if (!await applier.ApplyAsync(edit, editLabel, CancellationToken.None))
                    logger.LogWarning("workspace/applyEdit was rejected for story batch: {Label}", editLabel);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "workspace/applyEdit failed for story batch: {Label}", editLabel);
            }
        }
    }

    /// <summary>
    ///     One workspace edit for the whole batch: a create for each new file, then a
    ///     whole-document replacement per changed file (the composed final text). Composing many
    ///     commands' minimal edits would need offset rebasing after every step; replacing each
    ///     touched document with its final text is simpler and, for a deliberate Save, just as good.
    /// </summary>
    private static WorkspaceEdit BuildBatchEdit(
        IReadOnlyList<(string Uri, string? Original, string Current)> changed)
    {
        var changes = new List<WorkspaceEditDocumentChange>();

        // Creations first, so a freshly created thread/manifest exists before its content lands.
        foreach (var (uri, original, _) in changed)
            if (original is null)
                changes.Add(new WorkspaceEditDocumentChange(new CreateFile
                {
                    Uri = DocumentUri.From(uri),
                    Options = new CreateFileOptions { IgnoreIfExists = true }
                }));

        foreach (var (uri, original, current) in changed)
            changes.Add(new WorkspaceEditDocumentChange(new TextDocumentEdit
            {
                TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = DocumentUri.From(uri) },
                Edits = new TextEditContainer(new TextEdit
                {
                    Range = original is null ? new Range(0, 0, 0, 0) : WholeDocumentRange(original),
                    NewText = current
                })
            }));

        return new WorkspaceEdit { DocumentChanges = new Container<WorkspaceEditDocumentChange>(changes) };
    }

    private static Range WholeDocumentRange(string text)
    {
        var (line, col) = new LineOffsetIndex(text).GetPosition(text.Length);
        return new Range(0, 0, line, col);
    }
}