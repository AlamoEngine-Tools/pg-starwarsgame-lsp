// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Completion.Providers;

/// <summary>
///     Completion for engine type-69 tags - the comma-separated victory-condition lists on
///     <c>Campaign</c> (<c>Good_/Evil_/Human_/AI_Victory_Conditions</c>). Offers the tag's
///     schema-fixed enum values (<c>GalacticVictoryCondition</c>), completing the segment under the
///     cursor and excluding conditions already listed before it.
/// </summary>
public sealed class VictoryConditionProposalProvider : IXmlValueProposalProvider
{
    public XmlValueType ValueType => XmlValueType.Type69;

    public IReadOnlyList<ValueProposal> GetProposals(XmlTagDefinition tag, string partialValue, GameIndex? index = null)
    {
        if (tag.Enum is not { } enumDef)
            return [];

        var parts = partialValue.Split(',');
        var alreadySelected = parts
            .Take(parts.Length - 1)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentPartial = parts[^1].Trim();

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
