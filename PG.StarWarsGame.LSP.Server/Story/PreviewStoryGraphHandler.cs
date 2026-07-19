// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Story.Model;

namespace PG.StarWarsGame.LSP.Server.Story;

/// <summary>
///     Returns the campaign graph as it would look with the staged edit-mode batch applied, WITHOUT
///     writing to disk: the commands are composed onto an in-memory <see cref="WorkingTextSet" /> and
///     the campaign model is re-assembled reading each thread from that working copy (unchanged
///     threads fall back to buffer text). This lets the webview show structural edits - new/deleted/
///     renamed events, new prereq edges and their junctions - with the server doing the graph build,
///     so the client never re-implements it and the XML files stay untouched until Save.
/// </summary>
public sealed class PreviewStoryGraphHandler(
    IStoryModelService modelService,
    IGameIndexService indexService,
    IDocumentTextSource textSource,
    ISchemaProvider schema,
    IFileHelper fileHelper,
    IModProjectReloadService reloadService,
    ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<PreviewStoryGraphParams, GetStoryGraphResult>
{
    public Task<GetStoryGraphResult> Handle(PreviewStoryGraphParams request, CancellationToken ct)
    {
        if (StoryEditingFeature.Rejection(config) is { } rejection)
            return Task.FromResult(new GetStoryGraphResult([], [], rejection));

        var baseModel = modelService.GetCampaignModel(request.Campaign);
        if (baseModel is null)
            return Task.FromResult(new GetStoryGraphResult([], [],
                $"Campaign '{request.Campaign}' was not found."));

        // No staged commands → identical to the committed graph.
        if (request.Commands.Count == 0)
            return Task.FromResult(StoryGraphProjection.Project(
                baseModel, request.NameFilter, request.Branch, request.Lifecycle, request.ReachableFrom));

        var executor = new StoryCommandExecutor(
            modelService, indexService, textSource, schema, fileHelper, reloadService, NullLogger.Instance);
        var texts = new WorkingTextSet(textSource);

        var (failedIndex, error) = executor.Compose(
            baseModel, request.Commands.Select(c => c.ToParams(request.Campaign)).ToList(), texts);
        if (error is not null)
            return Task.FromResult(new GetStoryGraphResult([], [],
                $"Change {(failedIndex ?? 0) + 1} of {request.Commands.Count} can't be staged: {error}"));

        // Re-assemble the campaign over the composed working copy. The chain (campaign → manifest →
        // thread) comes from committed state; graph gestures only touch thread files, which the
        // reader serves from the working set (staged) or buffer (unchanged).
        var previewModel = new StoryCampaignAssembler(schema)
            .Assemble(request.Campaign, modelService.GetChainResult(), ReadThread);
        if (previewModel is null)
            return Task.FromResult(new GetStoryGraphResult([], [],
                $"Campaign '{request.Campaign}' could not be assembled from the staged changes."));

        return Task.FromResult(StoryGraphProjection.Project(
            previewModel, request.NameFilter, request.Branch, request.Lifecycle, request.ReachableFrom));

        (string Uri, string Text)? ReadThread(string xmlRelativePath)
        {
            var roots = reloadService.LastWorkspaceConfig?.XmlDirectories ?? [];
            foreach (var root in roots.Reverse()) // highest layer wins, mirroring StoryModelService
            {
                var path = fileHelper.FindInWorkspace([root], xmlRelativePath);
                if (path is null) continue;
                var uri = fileHelper.NormalizeUri(path);
                if (texts.GetText(uri) is { } text) return (uri, text);
            }

            return null;
        }
    }
}