// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Xml.CodeActions;

internal sealed class XmlCodeActionRegistry : IXmlCodeActionRegistry
{
    private readonly IReadOnlyList<IXmlCodeActionProvider> _providers;

    public XmlCodeActionRegistry(IEnumerable<IXmlCodeActionProvider> providers)
        => _providers = providers.ToList();

    public IEnumerable<CommandOrCodeAction> Dispatch(XmlCodeActionContext ctx)
        => _providers.SelectMany(p => p.Handle(ctx));
}
