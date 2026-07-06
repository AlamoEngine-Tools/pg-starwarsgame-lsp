// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Completion.Providers;

public sealed class DynamicEnumValueProposalProvider : IXmlValueProposalProvider
{
    private static readonly char[] FlagSeparators = ['|', ','];

    // Rebuilt only on DynamicEnumChanged (baseline/workspace enum-value applies) instead of
    // recomputing the baseline+workspace union from scratch on every completion keystroke.
    private IReadOnlyDictionary<string, string[]> _mergedValuesByEnum;

    public DynamicEnumValueProposalProvider(IGameIndexService indexService)
    {
        _mergedValuesByEnum = BuildCache(indexService.Current);
        indexService.DynamicEnumChanged += index => _mergedValuesByEnum = BuildCache(index);
    }

    private static IReadOnlyDictionary<string, string[]> BuildCache(GameIndex index)
    {
        var enumNames = index.Baseline.DynamicEnumValues.Keys
            .Concat(index.WorkspaceDynamicEnumValues.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var cache = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var enumName in enumNames)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var merged = new List<string>();

            if (index.Baseline.DynamicEnumValues.TryGetValue(enumName, out var baselineVals))
                foreach (var v in baselineVals)
                    if (seen.Add(v)) merged.Add(v);

            if (index.WorkspaceDynamicEnumValues.TryGetValue(enumName, out var workspaceVals))
                foreach (var v in workspaceVals)
                    if (seen.Add(v)) merged.Add(v);

            cache[enumName] = [.. merged];
        }

        return cache;
    }

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

        if (enumDef.Kind == EnumKind.DynamicXml)
            return GetDynamicProposals(enumDef.Name, currentPartial, alreadySelected);

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

    private IReadOnlyList<ValueProposal> GetDynamicProposals(
        string enumName, string currentPartial, HashSet<string> alreadySelected)
    {
        if (!_mergedValuesByEnum.TryGetValue(enumName, out var values))
            return [];

        return values
            .Where(v => !alreadySelected.Contains(v))
            .Where(v => v.StartsWith(currentPartial, StringComparison.OrdinalIgnoreCase))
            .Select(v => new ValueProposal { Label = v })
            .ToList();
    }
}