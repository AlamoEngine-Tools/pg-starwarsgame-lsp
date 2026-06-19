// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace PG.StarWarsGame.LSP.Xml.Completion;

internal sealed class XmlTagValueCompletionStrategyRegistry : IXmlTagValueCompletionStrategyRegistry
{
    private readonly IReadOnlyList<IXmlTagValueCompletionStrategy> _strategies;

    public XmlTagValueCompletionStrategyRegistry(IEnumerable<IXmlTagValueCompletionStrategy> strategies)
    {
        _strategies = strategies.ToList();
    }

    public IEnumerable<CompletionItem> GetCompletions(TagValueCompletionContext ctx)
    {
        return _strategies.SelectMany(s => s.Handle(ctx));
    }
}