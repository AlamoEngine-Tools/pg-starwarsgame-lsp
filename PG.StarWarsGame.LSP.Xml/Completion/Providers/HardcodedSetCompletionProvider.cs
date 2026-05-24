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

        var candidates = tag.ValueGroup is null
            ? set.Values
            : set.Values.Where(v =>
                v.Groups.Count == 0 ||
                v.Groups.Any(g => string.Equals(g, tag.ValueGroup, StringComparison.OrdinalIgnoreCase)));

        return candidates
            .Where(v => partialValue.Length == 0 ||
                        v.Name.StartsWith(partialValue, StringComparison.OrdinalIgnoreCase))
            .Select(v => new ValueProposal { Label = v.Name })
            .ToList();
    }
}