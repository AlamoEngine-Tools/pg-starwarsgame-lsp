// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.HoverStrategies;

internal sealed class ReferenceHoverStrategy : IXmlHoverStrategy
{
    public Hover? Handle(HoverContext ctx)
    {
        if (ctx.IsOnTagName)
            return null;

        if (!ctx.Index.Documents.TryGetValue(ctx.DocumentUri, out var docIndex))
            return null;

        var reference = docIndex.References.FirstOrDefault(r =>
            r.Line == ctx.Line && ctx.Character >= r.Column && ctx.Character < r.Column + r.Length);
        if (reference is null)
            return null;

        var symbol = ctx.Index.Resolve(reference.TargetId, reference.ExpectedTypeName);
        if (symbol?.TypeName is null)
            return null;

        var typeDef = ctx.Schema.GetObjectType(symbol.TypeName);
        if (typeDef is null)
            return null;

        // A navigable definition owned by a lower-ranked (dependency) layer gets an explicit
        // origin note; empty string = dependency layer without a display name.
        string? dependencyLayerName = null;
        if (symbol.Origin is FileOrigin { IsNavigable: true } fo
            && ctx.Index.Documents.TryGetValue(fo.Uri, out var originDoc)
            && originDoc.LayerRank != ctx.Index.LeafLayerRank)
            dependencyLayerName = originDoc.LayerName ?? string.Empty;

        var displayId = ReferenceResolutionEvaluator.StripOwnerPrefix(symbol.Id);
        return HoverUtility.BuildReferenceHover(typeDef, displayId, reference, ctx.Locale, symbol.Origin,
            dependencyLayerName);
    }
}