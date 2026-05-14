using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Completion.Providers;

public sealed class BooleanValueProposalProvider : IXmlValueProposalProvider
{
    private static readonly IReadOnlyList<ValueProposal> AllProposals =
    [
        new() { Label = "True" },
        new() { Label = "False" }
    ];

    public XmlValueType ValueType => XmlValueType.Boolean;

    public IReadOnlyList<ValueProposal> GetProposals(XmlTagDefinition tag, string partialValue)
    {
        if (string.IsNullOrEmpty(partialValue))
            return AllProposals;

        return AllProposals
            .Where(p => p.Label.StartsWith(partialValue, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}