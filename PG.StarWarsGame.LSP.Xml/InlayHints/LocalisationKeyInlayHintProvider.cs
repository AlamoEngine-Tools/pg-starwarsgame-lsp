// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.InlayHints;

internal sealed class LocalisationKeyInlayHintProvider : IXmlInlayHintProvider
{
    public IEnumerable<InlayHint> Handle(InlayHintContext ctx)
    {
        if (ctx.TagDef.ReferenceKind != ReferenceKind.LocalisationKey)
            return [];

        var key = ctx.Node.InnerText.Trim();
        if (string.IsNullOrEmpty(key))
            return [];

        var translated = ctx.Index.Localisation.GetValue(key) ?? key + ": MISSING";
        return
        [
            new InlayHint
            {
                Position = new Position(ctx.Line, int.MaxValue),
                Label = $"= \"{translated}\""!,
                Kind = InlayHintKind.Type,
                PaddingLeft = true
            }
        ];
    }
}
