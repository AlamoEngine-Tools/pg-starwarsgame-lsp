// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Xml.Completion;

internal sealed class XmlTagNameCompletionStrategyRegistry : IXmlTagNameCompletionStrategyRegistry
{
    private readonly IReadOnlyList<IXmlTagNameCompletionStrategy> _strategies;

    public XmlTagNameCompletionStrategyRegistry(IEnumerable<IXmlTagNameCompletionStrategy> strategies)
    {
        _strategies = strategies.ToList();
    }

    public IEnumerable<CompletionItem> GetCompletions(TagNameCompletionContext ctx)
    {
        return _strategies.SelectMany(s => s.Handle(ctx));
    }
}