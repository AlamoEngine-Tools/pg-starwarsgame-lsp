// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.JsonRpc;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Server.Variants;

/// <summary>
///     Resolves a variant GameObject against its <c>Variant_Of_Existing_Type</c> base chain and returns the
///     effective object rendered as annotated XML. The editor client opens the result in a read-only tab.
/// </summary>
public sealed class GetEffectiveObjectHandler
    : IJsonRpcRequestHandler<GetEffectiveObjectParams, GetEffectiveObjectResult>
{
    private readonly IGameIndexService _indexService;
    private readonly ISchemaProvider _schema;
    private readonly IVariantTagSource _tagSource;

    public GetEffectiveObjectHandler(IGameIndexService indexService, ISchemaProvider schema,
        IVariantTagSource tagSource)
    {
        _indexService = indexService;
        _schema = schema;
        _tagSource = tagSource;
    }

    public Task<GetEffectiveObjectResult> Handle(GetEffectiveObjectParams request,
        CancellationToken cancellationToken)
    {
        var resolver = new EffectiveObjectResolver(_indexService.Current, _schema, _tagSource);
        var effective = resolver.Resolve(request.ObjectId);

        if (!effective.Found)
            return Task.FromResult(new GetEffectiveObjectResult(false, false, null, [], string.Empty, null));

        return Task.FromResult(new GetEffectiveObjectResult(
            true,
            effective.Cyclic,
            effective.CycleObjectId,
            effective.Chain,
            EffectiveObjectXmlRenderer.Render(effective),
            effective.TypeName));
    }
}