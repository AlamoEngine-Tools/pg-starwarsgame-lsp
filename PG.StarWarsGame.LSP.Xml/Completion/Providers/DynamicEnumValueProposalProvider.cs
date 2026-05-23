// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Completion.Providers;

public sealed class DynamicEnumValueProposalProvider : IXmlValueProposalProvider
{
    private static readonly char[] FlagSeparators = ['|', ','];

    public XmlValueType ValueType => XmlValueType.DynamicEnumValue;

    public IReadOnlyList<ValueProposal> GetProposals(XmlTagDefinition tag, string partialValue)
    {
        if (tag.Enum is not { } enumDef)
            return [];

        var isFlagList = tag.SemanticType == TagSemanticType.FlagList;

        string currentPartial;
        HashSet<string> alreadySelected;

        if (isFlagList && partialValue.IndexOfAny(FlagSeparators) >= 0)
        {
            var parts = partialValue.Split(FlagSeparators);
            alreadySelected = parts
                .Take(parts.Length - 1)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            currentPartial = parts[^1].Trim();
        }
        else
        {
            alreadySelected = [];
            currentPartial = partialValue.Trim();
        }

        return enumDef.Values
            .Where(v => !alreadySelected.Contains(v.Name))
            .Where(v => v.Name.StartsWith(currentPartial, StringComparison.OrdinalIgnoreCase))
            .Select(v => new ValueProposal
            {
                Label = v.Name,
                Detail = v.Description.GetValueOrDefault("en")
            })
            .ToList();
    }
}
