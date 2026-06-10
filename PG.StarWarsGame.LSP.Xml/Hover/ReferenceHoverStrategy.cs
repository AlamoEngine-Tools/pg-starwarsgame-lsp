// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
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

        var symbol = ctx.Index.Resolve(reference.TargetId);
        if (symbol?.TypeName is null)
            return null;

        var typeDef = ctx.Schema.GetObjectType(symbol.TypeName);
        if (typeDef is null)
            return null;

        return HoverUtility.BuildReferenceHover(typeDef, symbol.Id, reference, ctx.Locale, symbol.Origin);
    }
}
