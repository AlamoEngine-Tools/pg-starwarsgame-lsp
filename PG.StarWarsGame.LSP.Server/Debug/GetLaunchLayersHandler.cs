// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.Debug;

public sealed class GetLaunchLayersHandler : IJsonRpcRequestHandler<GetLaunchLayersParams, GetLaunchLayersResult>
{
    private readonly ILspConfigurationProvider _config;
    private readonly IProjectLayerMap _layers;

    public GetLaunchLayersHandler(IProjectLayerMap layers, ILspConfigurationProvider config)
    {
        _layers = layers;
        _config = config;
    }

    public Task<GetLaunchLayersResult> Handle(GetLaunchLayersParams request, CancellationToken cancellationToken)
    {
        if (!_config.Current.Features.Lua.Debugger)
            return Task.FromResult(new GetLaunchLayersResult(false, []));

        var layers = _layers.Layers
            .OrderByDescending(layer => layer.Rank)
            .Select(layer =>
            {
                var (modPath, reason) = RunnableModLayout.Judge(layer);
                return new LaunchLayer(layer.Name, layer.Rank, layer.ProjectPath,
                    RunnableModLayout.ProjectDirectoryOf(layer), layer.ScriptRoots, modPath, reason);
            })
            .ToList();
        return Task.FromResult(new GetLaunchLayersResult(true, layers));
    }
}