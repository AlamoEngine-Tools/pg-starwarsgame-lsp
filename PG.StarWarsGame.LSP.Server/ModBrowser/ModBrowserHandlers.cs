// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Assets.Models;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
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
        var catalog = SymbolCatalog.For(index.Current, schema);
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
        var catalog = SymbolCatalog.For(current, schema);
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
    IFileHelper fileHelper,
    ILspConfigurationProvider config)
    : IJsonRpcRequestHandler<GetSymbolNodeParams, GetSymbolNodeResult>
{
    public Task<GetSymbolNodeResult> Handle(GetSymbolNodeParams request, CancellationToken cancellationToken)
    {
        if (!config.Current.Features.Tools.ModBrowser) return Task.FromResult(new GetSymbolNodeResult(null, []));
        var current = index.Current;
        var catalog = SymbolCatalog.For(current, schema);
        return Task.FromResult(new SymbolRelations(current, schema, tags, catalog, ModelProxies.For(current, assets),
                config.Current.Features.Tools, EntryFiles.For(assets, fileHelper))
            .Node(request.Page, request.Id));
    }
}

/// <summary>Serves one entry's preview: the tiles its type's adapter lists (#270).</summary>
public sealed class GetSymbolPreviewHandler(
    IGameIndexService index,
    ISchemaProvider schema,
    IVariantTagSource tags,
    IGameAssetResolver assets,
    IFileHelper fileHelper,
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
        var catalog = SymbolCatalog.For(current, schema);
        var relations = new SymbolRelations(current, schema, tags, catalog, ModelProxies.For(current, assets),
            features, EntryFiles.For(assets, fileHelper));
        return Task.FromResult(new SymbolPreviews(current, relations, texts, diagnostics, fileHelper.FileSystem,
                ModelProxies.BonesFor(current, assets), schema, config.Current.Locale)
            .Preview(request.Page, request.Id));
    }
}

/// <summary>
///     The file on disk an entry is: a script's document, or an asset where the asset resolver finds
///     it loose - in the project, or in the game's folder. One the resolver finds only inside an
///     archive has no file of its own.
/// </summary>
internal static class EntryFiles
{
    public static Func<string, string?> For(IGameAssetResolver assets, IFileHelper fileHelper)
    {
        // The resolver joins its roots and their contents with either separator; the path is handed
        // to the host's file manager, so it is made one clean path of the file system's own.
        return id => id.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            ? fileHelper.FileUriToPath(id)
            : assets.Locate(id) is { Tier: not GameAssetTier.Archive } location
                ? fileHelper.FileSystem.Path.GetFullPath(location.ResolvedPath)
                : null;
    }
}

/// <summary>
///     The proxy names inside a model file: from the catalog scan where it saw the model, otherwise
///     read once per model and index, when it is first opened.
/// </summary>
internal static class ModelProxies
{
    private static readonly ConditionalWeakTable<GameIndex, ConcurrentDictionary<string, ModelRead>>
        s_read = new();

    public static Func<string, IReadOnlyList<string>> For(GameIndex index, IGameAssetResolver assets)
    {
        var read = Reads(index, assets);
        return path => index.ModelProxies.TryGetValue(ModelBoneKey.From(path), out var scanned)
            ? scanned
            : read(path).Proxies;
    }

    /// <summary>
    ///     The bones of a model's own skeleton, from the same one read as its proxies; null for a
    ///     model that cannot be read.
    /// </summary>
    public static Func<string, int?> BonesFor(GameIndex index, IGameAssetResolver assets)
    {
        var read = Reads(index, assets);
        return path => read(path).Bones;
    }

    private static Func<string, ModelRead> Reads(GameIndex index, IGameAssetResolver assets)
    {
        var read = s_read.GetValue(index, _ => new ConcurrentDictionary<string, ModelRead>(
            StringComparer.OrdinalIgnoreCase));
        return path => read.GetOrAdd(path, p => Read(assets.Read(p)));
    }

    private static ModelRead Read(byte[]? bytes)
    {
        if (bytes is null || AloFile.Classify(bytes) != AloFileKind.Model) return new ModelRead([], null);
        try
        {
            var model = AloModelReader.Read(bytes);
            return new ModelRead(model.Proxies.Select(p => p.Name).ToList(), model.Bones.Count);
        }
        catch
        {
            // A model that cannot be read names no particle systems that can be shown;
            // ModelFileFormat is the diagnostic that owns "this .alo is not readable".
            return new ModelRead([], null);
        }
    }

    private sealed record ModelRead(IReadOnlyList<string> Proxies, int? Bones);
}