// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Xml.InlayHints;

internal abstract class LocalisationKeyInlayHintProviderBase : IXmlInlayHintProvider
{
    public IEnumerable<InlayHint> Handle(InlayHintContext ctx)
    {
        if (!IsResponsible(ctx))
            return [];

        var extractedValue = ctx.Node.InnerText.Trim();
        return string.IsNullOrEmpty(extractedValue) ? [] : HandleInternal(ctx, extractedValue);
    }

    protected abstract bool IsResponsible(InlayHintContext ctx);

    protected abstract IEnumerable<InlayHint> HandleInternal(InlayHintContext ctx, string translationKey);

    protected InlayHint CreateHintForTranslationKey(InlayHintContext ctx, string extractedValue)
    {
        var translated = ctx.Index.Localisation.GetValue(extractedValue) ?? extractedValue + ": MISSING";
        return new InlayHint
            {
                Position = new Position(ctx.Line, int.MaxValue),
                Label = $"\"{translated}\""!,
                Kind = InlayHintKind.Type,
                PaddingLeft = true
            }
            ;
    }
}