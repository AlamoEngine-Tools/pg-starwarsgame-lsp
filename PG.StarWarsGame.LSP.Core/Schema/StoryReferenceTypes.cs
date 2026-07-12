// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     The story-scoped <c>referenceType</c> names on <c>StoryEventType</c>/<c>StoryRewardType</c>
///     params and the symbol <c>TypeName</c> strings they map to. Story references resolve
///     campaign-scoped (an event name may legally repeat across threads and campaigns), so the
///     generic index-wide existence and duplicate validation must not own them — the campaign
///     graph diagnostics do.
/// </summary>
public static class StoryReferenceTypes
{
    // referenceType names in the YAML param definitions.
    public const string EventName = "StoryEventName";
    public const string Flag = "StoryFlag";
    public const string PlotFile = "StoryPlotFile";
    public const string Branch = "StoryBranch";
    public const string Notification = "StoryNotification";

    // Symbol TypeName strings (GameSymbol.TypeName / GameReference.ExpectedTypeName).
    public const string EventSymbol = "StoryEvent";
    public const string FlagSymbol = "StoryFlag";
    public const string NotificationSymbol = "StoryNotification";

    /// <summary>
    ///     The story thread file type from the metafile registry. The generic symbol pass indexes
    ///     every <c>&lt;Event&gt;</c> block as an object of this type.
    /// </summary>
    public const string ThreadFileTypeName = "StoryParser";

    /// <summary>Whether the referenceType belongs to the story domain (campaign-scoped semantics).</summary>
    public static bool IsStoryScoped(string referenceTypeName)
    {
        return referenceTypeName is EventName or Flag or PlotFile or Branch or Notification;
    }

    /// <summary>Whether the symbol TypeName is a story symbol (exempt from index-wide duplicate/existence checks).</summary>
    public static bool IsStorySymbolType(string? typeName)
    {
        return typeName is EventSymbol or FlagSymbol or NotificationSymbol;
    }

    /// <summary>((event/reward type, 0-based position) → referenceType) lookup over a schema enum.</summary>
    public static Dictionary<(string Type, int Position), string> BuildParamMap(EnumDefinition? definition)
    {
        var map = new Dictionary<(string, int), string>();
        foreach (var value in definition?.Values ?? [])
        foreach (var param in value.Params ?? (IReadOnlyList<ParamDefinition>)[])
            if (param.ReferenceTypeName is not null)
                map[(value.Name.ToUpperInvariant(), param.Position)] = param.ReferenceTypeName;
        return map;
    }

    /// <summary>Splits a story list param value (space/comma separated) into tokens.</summary>
    public static IEnumerable<string> SplitList(string rawValue)
    {
        return rawValue.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
    }
}
