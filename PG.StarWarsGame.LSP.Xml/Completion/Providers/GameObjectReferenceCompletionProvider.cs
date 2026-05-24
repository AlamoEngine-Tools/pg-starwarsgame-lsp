// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Completion.Providers;

public sealed class GameObjectReferenceCompletionProvider : IXmlCompletionProvider
{
    public bool CanHandle(XmlTagDefinition tag)
    {
        return tag.ReferenceKind == ReferenceKind.XmlObject;
    }

    public IReadOnlyList<ValueProposal> GetProposals(XmlTagDefinition tag, string partialValue, GameIndex index)
    {
        var typeName = tag.ObjectType?.TypeName;
        if (typeName is null) return [];

        var workspaceSymbols = index.WorkspaceDefinitions.Values.SelectMany(arr => arr);
        var baselineSymbols = index.Baseline.Symbols.Values;
        var isWildcard = string.Equals(typeName, "GameObjectType", StringComparison.OrdinalIgnoreCase);

        return workspaceSymbols.Concat(baselineSymbols)
            .Where(s => isWildcard || string.Equals(s.TypeName, typeName, StringComparison.OrdinalIgnoreCase))
            .Where(s => partialValue.Length == 0 ||
                        s.Id.StartsWith(partialValue, StringComparison.OrdinalIgnoreCase))
            .Select(s => new ValueProposal { Label = s.Id, Detail = s.Description })
            .ToList();
    }
}