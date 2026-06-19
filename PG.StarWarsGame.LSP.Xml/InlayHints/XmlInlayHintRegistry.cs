// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Xml.InlayHints;

internal sealed class XmlInlayHintRegistry : IXmlInlayHintRegistry
{
    private readonly IReadOnlyList<IXmlInlayHintProvider> _providers;

    public XmlInlayHintRegistry(IEnumerable<IXmlInlayHintProvider> providers)
    {
        _providers = providers.ToList();
    }

    public IEnumerable<InlayHint> Dispatch(InlayHintContext ctx)
    {
        return _providers.SelectMany(p => p.Handle(ctx));
    }
}