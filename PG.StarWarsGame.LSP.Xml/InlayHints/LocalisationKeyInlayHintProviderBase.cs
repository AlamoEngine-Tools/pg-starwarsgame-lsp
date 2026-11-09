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
        var translated = ctx.Index.Localisation.GetValue(extractedValue) ??
                         ctx.Messages.Format("LocalisationKeyInlayHint_Missing", extractedValue);
        // Anchored INSIDE the line, like every other hint. This used to be int.MaxValue, and the
        // editor compares hint positions as 32-bit integers (column = character + 1 overflows to a
        // negative number), so these texts sorted before everything else on their line - ahead of
        // the override marker, which the per-line length limit then cut off (#136).
        return new InlayHint
            {
                Position = new Position(ctx.Line, ctx.LineEndCharacter),
                Label = ((StringOrInlayHintLabelParts?)$"\"{translated}\"")!,
                Kind = InlayHintKind.Type,
                PaddingLeft = true
            }
            ;
    }
}