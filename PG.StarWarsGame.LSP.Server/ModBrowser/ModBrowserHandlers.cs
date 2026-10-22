// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Xml;

namespace PG.StarWarsGame.LSP.Server.ModBrowser;

/// <summary>
///     Serves an outline's pages with their counts, and the project's layers (#270). Gated on
///     <c>features.tools.modBrowser</c>, like the two requests below: off, each answers empty.
/// </summary>
public sealed class GetSymbolOutlineHandler(
    IGameIndexService index,
    ISchemaProvider schema,
    IProjectLayerMap layers,
    ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<GetSymbolOutlineParams, GetSymbolOutlineResult>
{
    public Task<GetSymbolOutlineResult> Handle(GetSymbolOutlineParams request, CancellationToken cancellationToken)
    {
        if (!config.Current.Features.Tools.ModBrowser) return Task.FromResult(new GetSymbolOutlineResult([], []));
        var catalog = SymbolCatalog.For(index.Current, schema.AllKinds, schema.AllEnums);
        return Task.FromResult(new GetSymbolOutlineResult(catalog.Outline(request.Outline),
            layers.Layers.Select(l => new SymbolLayer(l.Rank, l.Name)).ToList()));
    }
}

/// <summary>Serves one page of entries, or one relation of an entry, filtered and paged (#270).</summary>
public sealed class GetSymbolPageHandler(
    IGameIndexService index,
    ISchemaProvider schema,
    IVariantTagSource tags,
    IGameAssetResolver assets,
    ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<GetSymbolPageParams, GetSymbolPageResult>
{
    public Task<GetSymbolPageResult> Handle(GetSymbolPageParams request, CancellationToken cancellationToken)
    {
        if (!config.Current.Features.Tools.ModBrowser) return Task.FromResult(new GetSymbolPageResult([], 0));
        var current = index.Current;
        var catalog = SymbolCatalog.For(current, schema.AllKinds, schema.AllEnums);
        if (request.Parent is not { } parent) return Task.FromResult(catalog.Page(request));

        var rows = new SymbolRelations(current, schema, tags, catalog, ModelProxies.For(current, assets))
            .Rows(parent.Page, parent.Id, parent.Relation);
        return Task.FromResult(catalog.Filter(rows ?? [], request));
    }
}

/// <summary>Serves one entry, opened: its row and its relations, counted (#270).</summary>
public sealed class GetSymbolNodeHandler(
    IGameIndexService index,
    ISchemaProvider schema,
    IVariantTagSource tags,
    IGameAssetResolver assets,
    ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<GetSymbolNodeParams, GetSymbolNodeResult>
{
    public Task<GetSymbolNodeResult> Handle(GetSymbolNodeParams request, CancellationToken cancellationToken)
    {
        if (!config.Current.Features.Tools.ModBrowser) return Task.FromResult(new GetSymbolNodeResult(null, []));
        var current = index.Current;
        var catalog = SymbolCatalog.For(current, schema.AllKinds, schema.AllEnums);
        return Task.FromResult(new SymbolRelations(current, schema, tags, catalog, ModelProxies.For(current, assets),
                config.Current.Features.Tools)
            .Node(request.Page, request.Id));
    }
}

/// <summary>Serves one entry's preview: the tiles its type's adapter lists (#270).</summary>
public sealed class GetSymbolPreviewHandler(
    IGameIndexService index,
    ISchemaProvider schema,
    IVariantTagSource tags,
    IGameAssetResolver assets,
    IDocumentTextSource texts,
    IXmlDiagnosticsCollector diagnostics,
    ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<GetSymbolPreviewParams, GetSymbolPreviewResult>
{
    public Task<GetSymbolPreviewResult> Handle(GetSymbolPreviewParams request, CancellationToken cancellationToken)
    {
        var features = config.Current.Features.Tools;
        if (!features.ModBrowser) return Task.FromResult(new GetSymbolPreviewResult(null, []));
        var current = index.Current;
        var catalog = SymbolCatalog.For(current, schema.AllKinds, schema.AllEnums);
        var relations = new SymbolRelations(current, schema, tags, catalog, ModelProxies.For(current, assets),
            features);
        return Task.FromResult(new SymbolPreviews(current, schema, tags, relations, texts, diagnostics, features)
            .Preview(request.Page, request.Id));
    }
}

/// <summary>
///     The proxy names inside a model file, read once per model and index: the catalog scan does not
///     read proxies, so a model's particle systems cost one file read when it is first opened.
/// </summary>
internal static class ModelProxies
{
    private static readonly ConditionalWeakTable<GameIndex, ConcurrentDictionary<string, IReadOnlyList<string>>>
        s_read = new();

    public static Func<string, IReadOnlyList<string>> For(GameIndex index, IGameAssetResolver assets)
    {
        var read = s_read.GetValue(index, _ => new ConcurrentDictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase));
        return path => read.GetOrAdd(path, p => Proxies(assets.Read(p)));
    }

    private static IReadOnlyList<string> Proxies(byte[]? bytes)
    {
        if (bytes is null || AloFile.Classify(bytes) != AloFileKind.Model) return [];
        try
        {
            return AloModelReader.Read(bytes).Proxies.Select(p => p.Name).ToList();
        }
        catch
        {
            // A model that cannot be read names no particle systems that can be shown;
            // ModelFileFormat is the diagnostic that owns "this .alo is not readable".
            return [];
        }
    }
}