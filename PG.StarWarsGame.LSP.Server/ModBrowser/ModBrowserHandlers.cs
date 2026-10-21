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

/// <summary>Serves one page of entries, or one relation of an entry, filtered and paged (#270).</summary>
public sealed class GetSymbolPageHandler(IGameIndexService index, ISchemaProvider schema, IVariantTagSource tags)
    : IJsonRpcRequestHandler<GetSymbolPageParams, GetSymbolPageResult>
{
    public Task<GetSymbolPageResult> Handle(GetSymbolPageParams request, CancellationToken cancellationToken)
    {
        var current = index.Current;
        var catalog = SymbolCatalog.For(current, schema.AllKinds, schema.AllEnums);
        if (request.Parent is not { } parent) return Task.FromResult(catalog.Page(request));

        var rows = new SymbolRelations(current, schema, tags, catalog).Rows(parent.Page, parent.Id, parent.Relation);
        return Task.FromResult(SymbolCatalog.Filter(rows ?? [], request));
    }
}

/// <summary>Serves one entry, opened: its row and its relations, counted (#270).</summary>
public sealed class GetSymbolNodeHandler(IGameIndexService index, ISchemaProvider schema, IVariantTagSource tags)
    : IJsonRpcRequestHandler<GetSymbolNodeParams, GetSymbolNodeResult>
{
    public Task<GetSymbolNodeResult> Handle(GetSymbolNodeParams request, CancellationToken cancellationToken)
    {
        var current = index.Current;
        var catalog = SymbolCatalog.For(current, schema.AllKinds, schema.AllEnums);
        return Task.FromResult(new SymbolRelations(current, schema, tags, catalog).Node(request.Page, request.Id));
    }
}