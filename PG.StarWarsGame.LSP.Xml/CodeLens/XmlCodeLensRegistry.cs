// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using LspCodeLens = OmniSharp.Extensions.LanguageServer.Protocol.Models.CodeLens;

namespace PG.StarWarsGame.LSP.Xml.CodeLens;

internal sealed class XmlCodeLensRegistry : IXmlCodeLensRegistry
{
    private readonly IReadOnlyList<IXmlCodeLensProvider> _providers;

    public XmlCodeLensRegistry(IEnumerable<IXmlCodeLensProvider> providers)
    {
        _providers = providers.ToList();
    }

    public IEnumerable<LspCodeLens> Dispatch(CodeLensSymbolContext ctx)
    {
        return _providers.Select(p => p.Handle(ctx)).Where(l => l is not null).Select(l => l!);
    }
}