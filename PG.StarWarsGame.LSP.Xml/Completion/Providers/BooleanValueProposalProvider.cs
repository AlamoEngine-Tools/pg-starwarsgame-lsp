// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Completion.Providers;

public sealed class BooleanValueProposalProvider : IXmlValueProposalProvider
{
    private static readonly IReadOnlyList<ValueProposal> AllProposals =
    [
        new() { Label = "True" },
        new() { Label = "False" }
    ];

    public XmlValueType ValueType => XmlValueType.Boolean;

    public IReadOnlyList<ValueProposal> GetProposals(XmlTagDefinition tag, string partialValue, GameIndex? index = null)
    {
        if (string.IsNullOrEmpty(partialValue))
            return AllProposals;

        return AllProposals
            .Where(p => p.Label.StartsWith(partialValue, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}