// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Story.Graph;
using PG.StarWarsGame.LSP.Story.Model;
using PG.StarWarsGame.LSP.Xml;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     The on-demand Validate action for edit mode. Applies the staged command batch to an in-memory
///     working copy (via <see cref="StoryCommandExecutor" /> over a <see cref="WorkingTextSet" />) —
///     nothing is written — then runs the XML diagnostics collector over the composed texts and
///     correlates the results to graph nodes by name, so the problems reflect exactly the pending
///     state the user is about to save. A staged command that fails to validate short-circuits with
///     that error (the pending graph is internally inconsistent and can't be composed).
/// </summary>
public sealed class ValidateStoryCommandBatchHandler(
    IStoryModelService modelService,
    IGameIndexService indexService,
    IDocumentTextSource textSource,
    ISchemaProvider schema,
    IFileHelper fileHelper,
    IModProjectReloadService reloadService,
    IXmlDiagnosticsCollector collector,
    ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<ValidateStoryCommandBatchParams, GetStoryDiagnosticsResult>
{
    public Task<GetStoryDiagnosticsResult> Handle(
        ValidateStoryCommandBatchParams request, CancellationToken ct)
    {
        if (StoryEditorFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new GetStoryDiagnosticsResult([], rejection));

        var model = modelService.GetCampaignModel(request.Campaign);
        if (model is null)
            return Task.FromResult(new GetStoryDiagnosticsResult([],
                $"Campaign '{request.Campaign}' was not found."));

        var executor = new StoryCommandExecutor(
            modelService, indexService, textSource, schema, fileHelper, reloadService,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var texts = new WorkingTextSet(textSource);

        var (failedIndex, composeError) = executor.Compose(
            model, request.Commands.Select(c => c.ToParams(request.Campaign)).ToList(), texts);
        if (composeError is not null)
            return Task.FromResult(new GetStoryDiagnosticsResult([],
                $"Change {(failedIndex ?? 0) + 1} of {request.Commands.Count} can't be staged: {composeError}"));

        // Validate every thread the campaign knows about (reading its staged text where present),
        // plus any new thread the batch created that already has events.
        var modelThreadUris = new HashSet<string>(
            model.Threads.Select(t => fileHelper.NormalizeUri(t.DocumentUri)), StringComparer.Ordinal);
        var candidates = new HashSet<string>(modelThreadUris, StringComparer.Ordinal);
        foreach (var (uri, _, _) in texts.Changed())
            candidates.Add(fileHelper.NormalizeUri(uri));

        var index = indexService.Current;
        var results = new List<StoryDiagnosticDto>();
        foreach (var uri in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var text = texts.GetText(uri);
            if (text is null) continue;

            var thread = StoryThreadParser.Parse(text, uri);
            // Skip changed non-thread files (manifests, campaign sets) — they carry no events, so
            // there is nothing to pin to the story graph.
            if (!modelThreadUris.Contains(uri) && thread.Events.Count == 0) continue;

            StoryDiagnosticsCorrelator.Correlate(
                uri, thread.Events,
                collector.Collect(uri, text, index),
                e => StoryGraphBuilder.EventNodeId(uri, e.Name),
                results);
        }

        return Task.FromResult(new GetStoryDiagnosticsResult(results));
    }
}
