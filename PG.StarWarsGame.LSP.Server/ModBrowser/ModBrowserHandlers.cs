// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Server.ModBrowser;

/// <summary>Serves an outline's pages with their counts, and the project's layers (#270).</summary>
public sealed class GetSymbolOutlineHandler(IGameIndexService index, ISchemaProvider schema, IProjectLayerMap layers)
    : IJsonRpcRequestHandler<GetSymbolOutlineParams, GetSymbolOutlineResult>
{
    public Task<GetSymbolOutlineResult> Handle(GetSymbolOutlineParams request, CancellationToken cancellationToken)
    {
        var catalog = SymbolCatalog.For(index.Current, schema.AllKinds, schema.AllEnums);
        return Task.FromResult(new GetSymbolOutlineResult(catalog.Outline(request.Outline),
            layers.Layers.Select(l => new SymbolLayer(l.Rank, l.Name)).ToList()));
    }
}

/// <summary>Serves one page of entries, filtered and paged (#270).</summary>
public sealed class GetSymbolPageHandler(IGameIndexService index, ISchemaProvider schema)
    : IJsonRpcRequestHandler<GetSymbolPageParams, GetSymbolPageResult>
{
    public Task<GetSymbolPageResult> Handle(GetSymbolPageParams request, CancellationToken cancellationToken)
    {
        return Task.FromResult(SymbolCatalog.For(index.Current, schema.AllKinds, schema.AllEnums).Page(request));
    }
}