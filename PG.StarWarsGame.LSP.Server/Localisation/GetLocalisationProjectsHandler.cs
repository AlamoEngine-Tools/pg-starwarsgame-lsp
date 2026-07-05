// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Configuration;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class GetLocalisationProjectsHandler
    : IJsonRpcRequestHandler<GetLocalisationProjectsParams, GetLocalisationProjectsResult>
{
    private readonly ILocalisationProjectRegistry _registry;
    private readonly ILspConfigurationProvider _config;

    public GetLocalisationProjectsHandler(ILocalisationProjectRegistry registry, ILspConfigurationProvider config)
    {
        _registry = registry;
        _config = config;
    }

    public Task<GetLocalisationProjectsResult> Handle(
        GetLocalisationProjectsParams request, CancellationToken cancellationToken)
    {
        if (!_config.Current.Features.Tools.Localisation)
            return Task.FromResult(new GetLocalisationProjectsResult([]));

        return Task.FromResult(new GetLocalisationProjectsResult(_registry.Projects));
    }
}