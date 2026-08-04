// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Completion;

public interface IXmlValueProposalRegistry
{
    IReadOnlyList<ValueProposal> GetProposals(XmlValueType valueType, XmlTagDefinition tag, string partialValue,
        GameIndex? index = null);
}