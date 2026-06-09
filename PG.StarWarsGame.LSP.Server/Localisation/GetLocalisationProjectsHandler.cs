// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed class GetLocalisationProjectsHandler
    : IJsonRpcRequestHandler<GetLocalisationProjectsParams, GetLocalisationProjectsResult>
{
    private readonly ILocalisationProjectRegistry _registry;

    public GetLocalisationProjectsHandler(ILocalisationProjectRegistry registry)
    {
        _registry = registry;
    }

    public Task<GetLocalisationProjectsResult> Handle(
        GetLocalisationProjectsParams request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new GetLocalisationProjectsResult(_registry.Projects));
    }
}
