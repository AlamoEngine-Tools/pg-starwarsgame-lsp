// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Completion;

public sealed class StoryParamValueProposalProvider(ISchemaProvider schema)
{
    public IReadOnlyList<ValueProposal> GetProposals(
        ParamDefinition? def, string partialValue, GameIndex index)
    {
        if (def is null) return [];
        return def.ValueType switch
        {
            XmlValueType.DynamicEnumValue => GetEnumProposals(def.EnumName, partialValue),
            XmlValueType.Boolean => GetBooleanIntProposals(partialValue),
            XmlValueType.NameReference or XmlValueType.NameReferenceList =>
                GetRefProposals(def.ReferenceType, partialValue, index),
            _ => []
        };
    }

    private IReadOnlyList<ValueProposal> GetEnumProposals(string? enumName, string partialValue)
    {
        if (enumName is null) return [];
        var enumDef = schema.GetEnum(enumName);
        if (enumDef is null) return [];
        return enumDef.Values
            .Where(v => partialValue.Length == 0 ||
                        v.Name.StartsWith(partialValue, StringComparison.OrdinalIgnoreCase))
            .Select(v => new ValueProposal { Label = v.Name })
            .ToList();
    }

    private static IReadOnlyList<ValueProposal> GetBooleanIntProposals(string partialValue)
    {
        string[] candidates = ["0", "1"];
        return candidates
            .Where(v => partialValue.Length == 0 ||
                        v.StartsWith(partialValue, StringComparison.OrdinalIgnoreCase))
            .Select(v => new ValueProposal { Label = v })
            .ToList();
    }

    private static IReadOnlyList<ValueProposal> GetRefProposals(
        string? referenceType, string partialValue, GameIndex index)
    {
        if (referenceType is null) return [];
        var workspaceSymbols = index.WorkspaceDefinitions.Values
            .SelectMany(arr => arr);
        var baselineSymbols = index.Baseline.Symbols.Values;

        return workspaceSymbols.Concat(baselineSymbols)
            .Where(s => string.Equals(s.TypeName, referenceType, StringComparison.OrdinalIgnoreCase))
            .Where(s => partialValue.Length == 0 ||
                        s.Id.StartsWith(partialValue, StringComparison.OrdinalIgnoreCase))
            .Select(s => new ValueProposal { Label = s.Id, Detail = s.Description })
            .ToList();
    }
}