// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Completion;

public sealed class XmlCompletionRegistry(IEnumerable<IXmlCompletionProvider> providers)
    : IXmlCompletionRegistry
{
    private readonly IReadOnlyList<IXmlCompletionProvider> _providers = [.. providers];

    public IReadOnlyList<ValueProposal> GetProposals(XmlTagDefinition tag, string partialValue, GameIndex index)
    {
        var provider = _providers.FirstOrDefault(p => p.CanHandle(tag));
        return provider?.GetProposals(tag, partialValue, index) ?? [];
    }
}