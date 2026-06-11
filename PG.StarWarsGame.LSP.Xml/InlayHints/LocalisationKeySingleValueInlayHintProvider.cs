// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.InlayHints;

internal sealed class LocalisationKeySingleValueInlayHintProvider : LocalisationKeyInlayHintProviderBase
{
    protected override bool IsResponsible(InlayHintContext ctx)
    {
        return ctx.TagDef is { ReferenceKind: ReferenceKind.LocalisationKey, ValueType: XmlValueType.NameReference };
    }

    protected override IEnumerable<InlayHint> HandleInternal(InlayHintContext ctx, string translationKey)
    {
        return
        [
            CreateHintForTranslationKey(ctx, translationKey)
        ];
    }
}