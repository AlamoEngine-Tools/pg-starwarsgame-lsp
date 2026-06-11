// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Xml.Util;

namespace PG.StarWarsGame.LSP.Xml.InlayHints;

internal sealed class LocalisationKeyMultiValueInlayHintProvider : LocalisationKeyInlayHintProviderBase
{
    protected override bool IsResponsible(InlayHintContext ctx)
    {
        return ctx.TagDef is
        {
            ReferenceKind: ReferenceKind.LocalisationKey,
            ValueType: XmlValueType.TypeReferenceList or XmlValueType.NameReferenceList
        };
    }

    protected override IEnumerable<InlayHint> HandleInternal(InlayHintContext ctx, string translationKey)
    {
        return ListValueConstants.PrepareValueForSplit(translationKey)
            .Split(ListValueConstants.GetListSeparators(), StringSplitOptions.RemoveEmptyEntries)
            .Select(key => CreateHintForTranslationKey(ctx, key));
    }
}