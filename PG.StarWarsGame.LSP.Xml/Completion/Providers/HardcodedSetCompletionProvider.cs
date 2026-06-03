// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Completion.Providers;

public sealed class HardcodedSetCompletionProvider : IXmlCompletionProvider
{
    public bool CanHandle(XmlTagDefinition tag)
    {
        return tag.ReferenceKind == ReferenceKind.HardcodedSet && tag.HardcodedSet is not null;
    }

    public IReadOnlyList<ValueProposal> GetProposals(XmlTagDefinition tag, string partialValue, GameIndex index)
    {
        var set = tag.HardcodedSet!;
        var groups = tag.ValueGroups;

        var candidates = groups.Count == 0
            ? set.Values
            : set.Values.Where(v =>
                v.Groups.Count == 0 ||
                v.Groups.Any(g => groups.Contains(g, StringComparer.OrdinalIgnoreCase)));

        if (groups.Count > 0)
            candidates = candidates.OrderBy(v => GroupRank(v, groups));

        return candidates
            .Where(v => partialValue.Length == 0 ||
                        v.Name.StartsWith(partialValue, StringComparison.OrdinalIgnoreCase))
            .Select(v => new ValueProposal { Label = v.Name })
            .ToList();
    }

    private static int GroupRank(HardcodedReferenceSetValue value, IReadOnlyList<string> groups)
    {
        for (var i = 0; i < groups.Count; i++)
            if (value.Groups.Any(g => string.Equals(g, groups[i], StringComparison.OrdinalIgnoreCase)))
                return i;
        return int.MaxValue; // empty-groups (universal) values rank last
    }
}