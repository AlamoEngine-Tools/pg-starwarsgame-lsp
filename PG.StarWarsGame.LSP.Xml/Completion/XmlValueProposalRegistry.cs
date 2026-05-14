using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Completion;

public sealed class XmlValueProposalRegistry : IXmlValueProposalRegistry
{
    private readonly IReadOnlyDictionary<XmlValueType, IXmlValueProposalProvider> _providers;

    public XmlValueProposalRegistry(IEnumerable<IXmlValueProposalProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.ValueType);
    }

    public IReadOnlyList<ValueProposal> GetProposals(
        XmlValueType valueType, XmlTagDefinition tag, string partialValue)
    {
        if (!_providers.TryGetValue(valueType, out var provider))
            return [];

        return provider.GetProposals(tag, partialValue);
    }
}