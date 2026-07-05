// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class GetRootLocalisationConfigHandler
    : IJsonRpcRequestHandler<GetRootLocalisationConfigParams, GetRootLocalisationConfigResult>
{
    private readonly IModProjectReloadService _reloadService;
    private readonly ILspConfigurationProvider _config;

    public GetRootLocalisationConfigHandler(IModProjectReloadService reloadService, ILspConfigurationProvider config)
    {
        _reloadService = reloadService;
        _config = config;
    }

    public Task<GetRootLocalisationConfigResult> Handle(
        GetRootLocalisationConfigParams request, CancellationToken ct)
    {
        if (!_config.Current.Features.Tools.Localisation)
            return Task.FromResult(GetRootLocalisationConfigResult.NotConfigured);

        var rootLayer = _reloadService.LastWorkspaceConfig?.Layers
            .OrderByDescending(l => l.Rank).FirstOrDefault();

        if (rootLayer is { TextResourceType: { } type, TextRoots.Count: > 0 })
            return Task.FromResult(new GetRootLocalisationConfigResult(true, type, rootLayer.TextRoots[0]));

        return Task.FromResult(GetRootLocalisationConfigResult.NotConfigured);
    }
}
