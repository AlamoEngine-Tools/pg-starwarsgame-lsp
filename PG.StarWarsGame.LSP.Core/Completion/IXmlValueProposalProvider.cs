// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Completion;

public interface IXmlValueProposalProvider
{
    XmlValueType ValueType { get; }
    /// <param name="index">
    ///     The index of the project the completion is for. Providers whose proposals come from
    ///     workspace data (dynamic enum values) must answer from it - in a multi-root workspace the
    ///     values differ per mod. Null keeps the primary project's answer.
    /// </param>
    IReadOnlyList<ValueProposal> GetProposals(XmlTagDefinition tag, string partialValue, GameIndex? index = null);
}