// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Completion;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Xml.Completion;

public sealed class StoryParamValueProposalProvider
{
    public IReadOnlyList<ValueProposal> GetProposals(
        ParamDefinition? def, string partialValue, GameIndex index)
    {
        if (def is null) return [];
        return def.ValueType switch
        {
            XmlValueType.DynamicEnumValue => GetEnumProposals(def.Enum, partialValue),
            XmlValueType.Boolean => GetBooleanIntProposals(partialValue),
            XmlValueType.NameReference or XmlValueType.NameReferenceList =>
                GetRefProposals(def, partialValue, index),
            _ => []
        };
    }

    private static IReadOnlyList<ValueProposal> GetEnumProposals(EnumDefinition? enumDef, string partialValue)
    {
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
        ParamDefinition def, string partialValue, GameIndex index)
    {
        // ObjectType is only resolved when the referenceType is a types.yaml object type; story
        // params carry only the raw referenceType string — fall back to it.
        var referenceType = def.ObjectType?.TypeName ?? def.ReferenceTypeName;
        if (referenceType is null) return [];

        // Story-scoped referenceTypes map to their index symbol TypeNames (StoryBranch and
        // StoryPlotFile have no index symbols and simply match nothing here).
        var typeName = referenceType switch
        {
            StoryReferenceTypes.EventName => StoryReferenceTypes.EventSymbol,
            StoryReferenceTypes.Notification => StoryReferenceTypes.NotificationSymbol,
            _ => referenceType
        };

        // "GameObjectType" is the umbrella over every concrete object type — no symbol carries it
        // as its literal TypeName (mirrors GameObjectReferenceCompletionProvider's wildcard).
        // Story symbols and thread-file objects are excluded: an event name is never a valid
        // object reference even though it lives in the same index.
        var isWildcard = string.Equals(typeName, "GameObjectType", StringComparison.OrdinalIgnoreCase);

        var workspaceSymbols = index.WorkspaceDefinitions.Values
            .SelectMany(arr => arr);
        var baselineSymbols = index.Baseline.Symbols.Values;

        return workspaceSymbols.Concat(baselineSymbols)
            .Where(s => isWildcard
                ? s.Kind == GameSymbolKind.XmlObject
                  && !StoryReferenceTypes.IsStorySymbolType(s.TypeName)
                  && !string.Equals(s.TypeName, StoryReferenceTypes.ThreadFileTypeName,
                      StringComparison.OrdinalIgnoreCase)
                : string.Equals(s.TypeName, typeName, StringComparison.OrdinalIgnoreCase))
            // Scoped ability IDs are stored as "OWNER$name"; propose and filter the bare name.
            .Select(s => (Symbol: s, DisplayId: ReferenceResolutionEvaluator.StripOwnerPrefix(s.Id)))
            .Where(t => partialValue.Length == 0 ||
                        t.DisplayId.StartsWith(partialValue, StringComparison.OrdinalIgnoreCase))
            .Select(t => new ValueProposal { Label = t.DisplayId, Detail = t.Symbol.Description })
            .Distinct()
            .ToList();
    }
}