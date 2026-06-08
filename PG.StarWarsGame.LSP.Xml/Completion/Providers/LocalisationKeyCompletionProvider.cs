// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Completion.Providers;

public sealed class LocalisationKeyCompletionProvider : IXmlCompletionProvider
{
    public bool CanHandle(XmlTagDefinition tag)
    {
        return tag.ReferenceKind == ReferenceKind.LocalisationKey;
    }

    public IReadOnlyList<ValueProposal> GetProposals(XmlTagDefinition tag, string partialValue, GameIndex index)
    {
        return index.Localisation.Keys
            .Where(k => partialValue.Length == 0 ||
                        k.StartsWith(partialValue, StringComparison.OrdinalIgnoreCase))
            .Select(k => new ValueProposal { Label = k })
            .ToList();
    }
}