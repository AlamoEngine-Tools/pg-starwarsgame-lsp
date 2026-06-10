// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.HoverStrategies;

internal sealed class AssetHoverStrategy : IXmlHoverStrategy
{
    public Hover? Handle(HoverContext ctx)
    {
        if (ctx.IsOnTagName)
            return null;

        var tagDef = ctx.Schema.GetTag(ctx.Node.Name);
        if (tagDef is null || tagDef.ReferenceKind is not (
                ReferenceKind.TextureFile or ReferenceKind.ModelFile or
                ReferenceKind.AudioFile or ReferenceKind.MapFile))
            return null;

        var value = ctx.Node.InnerText.Trim();
        if (string.IsNullOrEmpty(value))
            return null;

        return HoverUtility.BuildAssetReferenceHover(
            tagDef, value, ctx.Index.AssetFiles, ctx.Line, ctx.Character, value.Length);
    }
}
