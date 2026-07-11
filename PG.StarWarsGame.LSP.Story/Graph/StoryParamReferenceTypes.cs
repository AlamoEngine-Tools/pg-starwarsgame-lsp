// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Story.Graph;

/// <summary>
///     The <c>referenceType</c> names that drive story graph semantics, plus the schema lookup
///     ((event/reward type, 0-based position) → referenceType) shared by the graph builder and
///     the graph diagnostics.
/// </summary>
internal static class StoryParamReferenceTypes
{
    public const string EventName = "StoryEventName";
    public const string Flag = "StoryFlag";
    public const string PlotFile = "StoryPlotFile";
    public const string Branch = "StoryBranch";

    public static Dictionary<(string Type, int Position), string> Build(EnumDefinition? definition)
    {
        var map = new Dictionary<(string, int), string>();
        foreach (var value in definition?.Values ?? [])
        foreach (var param in value.Params ?? (IReadOnlyList<ParamDefinition>)[])
            if (param.ReferenceTypeName is not null)
                map[(value.Name.ToUpperInvariant(), param.Position)] = param.ReferenceTypeName;
        return map;
    }

    public static IEnumerable<string> SplitList(string rawValue)
    {
        return rawValue.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
    }
}
