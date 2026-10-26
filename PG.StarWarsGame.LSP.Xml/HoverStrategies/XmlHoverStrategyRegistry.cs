// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Xml.HoverStrategies;

internal sealed class XmlHoverStrategyRegistry : IXmlHoverStrategyRegistry
{
    private readonly IReadOnlyList<IXmlHoverStrategy> _strategies;

    public XmlHoverStrategyRegistry(IEnumerable<IXmlHoverStrategy> strategies)
    {
        _strategies = strategies.ToList();
    }

    public Hover? Dispatch(HoverContext ctx)
    {
        return _strategies.Select(s => s.Handle(ctx)).FirstOrDefault(h => h is not null);
    }
}