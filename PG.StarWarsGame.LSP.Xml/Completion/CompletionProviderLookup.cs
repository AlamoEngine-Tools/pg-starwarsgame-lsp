// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Completion;

/// <summary>
///     How a value-completion strategy picks ONE provider out of the injected collections (#312):
///     the first <see cref="IXmlCompletionProvider" /> that can handle the tag, and the one
///     <see cref="IXmlValueProposalProvider" /> declared for the value type. No provider gives no
///     proposals.
/// </summary>
public static class CompletionProviderLookup
{
    public static IReadOnlyList<ValueProposal> GetProposals(this IEnumerable<IXmlCompletionProvider> providers,
        XmlTagDefinition tag, string partialValue, GameIndex index)
    {
        return providers.FirstOrDefault(p => p.CanHandle(tag))?.GetProposals(tag, partialValue, index) ?? [];
    }

    public static IReadOnlyList<ValueProposal> GetProposals(this IEnumerable<IXmlValueProposalProvider> providers,
        XmlValueType valueType, XmlTagDefinition tag, string partialValue)
    {
        return providers.FirstOrDefault(p => p.ValueType == valueType)?.GetProposals(tag, partialValue) ?? [];
    }
}