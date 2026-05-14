using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Completion;

public interface IXmlValueProposalRegistry
{
    IReadOnlyList<ValueProposal> GetProposals(XmlValueType valueType, XmlTagDefinition tag, string partialValue);
}