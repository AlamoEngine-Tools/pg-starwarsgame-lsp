// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Story.Graph;

namespace PG.StarWarsGame.LSP.Server;

/// <summary>
///     Serves the story graph diagnostics for a document by running the producer over every
///     campaign model containing it. A thread shared by several campaigns (universal triggers)
///     would repeat identical findings - they are de-duplicated; campaign-specific findings
///     (e.g. ambiguity that exists in only one campaign) survive. Gated by
///     <c>features.story.graphDiagnostics</c>.
/// </summary>
public sealed class StoryGraphDiagnosticsService : IStoryGraphDiagnosticsSource
{
    private readonly ILspConfigurationProvider _configProvider;
    private readonly IStoryModelService _modelService;
    private readonly StoryGraphDiagnosticsProducer _producer;

    public StoryGraphDiagnosticsService(
        IStoryModelService modelService,
        ISchemaProvider schema,
        ILspConfigurationProvider configProvider)
    {
        _modelService = modelService;
        _configProvider = configProvider;
        _producer = new StoryGraphDiagnosticsProducer(schema);
    }

    public IReadOnlyList<StoryGraphDiagnostic> GetForDocument(string canonicalUri)
    {
        if (!_configProvider.Current.Features.Story.GraphDiagnostics) return [];

        var seen = new HashSet<(int, int, string)>();
        var diagnostics = new List<StoryGraphDiagnostic>();
        foreach (var model in _modelService.GetModelsContaining(canonicalUri))
        foreach (var diagnostic in _producer.Produce(model, canonicalUri))
            if (seen.Add((diagnostic.Line, diagnostic.Column, diagnostic.Message)))
                diagnostics.Add(diagnostic);

        return diagnostics;
    }
}