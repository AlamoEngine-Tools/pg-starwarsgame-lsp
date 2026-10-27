// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Debug;

public sealed class GetLaunchLayersHandler : IJsonRpcRequestHandler<GetLaunchLayersParams, GetLaunchLayersResult>
{
    private readonly IMessageCatalog _messages;
    private readonly IProjectLayerMap _layers;

    public GetLaunchLayersHandler(IProjectLayerMap layers, IMessageCatalog messages)
    {
        _layers = layers;
        _messages = messages;
    }

    public Task<GetLaunchLayersResult> Handle(GetLaunchLayersParams request, CancellationToken cancellationToken)
    {
        var layers = _layers.Layers
            .OrderByDescending(layer => layer.Rank)
            .Select(layer =>
            {
                var (modPath, reason) = RunnableModLayout.Judge(layer, _messages);
                return new LaunchLayer(layer.Name, layer.Rank, layer.ProjectPath,
                    RunnableModLayout.ProjectDirectoryOf(layer), layer.ScriptRoots, modPath, reason);
            })
            .ToList();
        return Task.FromResult(new GetLaunchLayersResult(true, layers));
    }
}