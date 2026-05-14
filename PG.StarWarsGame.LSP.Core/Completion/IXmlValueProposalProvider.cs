using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Completion;

public interface IXmlValueProposalProvider
{
    XmlValueType ValueType { get; }
    IReadOnlyList<ValueProposal> GetProposals(XmlTagDefinition tag, string partialValue);
}