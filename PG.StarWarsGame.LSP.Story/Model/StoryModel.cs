// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Story.Model;

/// <summary>A 0-based source span, possibly crossing lines.</summary>
public sealed record StorySourceRange(int StartLine, int StartColumn, int EndLine, int EndColumn)
{
    public static readonly StorySourceRange None = new(-1, -1, -1, -1);
}

/// <summary>A single value token with its exact source span (rename/diagnostics anchor).</summary>
public sealed record StoryToken(string Text, StorySourceRange Range);

/// <summary>
///     One child element of an <c>&lt;Event&gt;</c> block, in document order, with the original
///     source casing - the substrate for the canonical-tag-order validation and the future
///     minimal-edit writer.
/// </summary>
public sealed record StoryEventTag(string Name, string Value, StorySourceRange ValueRange);

/// <summary>One <c>&lt;Prereq&gt;</c> line: its tokens are AND-ed; multiple lines are OR-ed.</summary>
public sealed record StoryPrereqGroup(IReadOnlyList<StoryToken> Tokens, StorySourceRange Range);

/// <summary>A present <c>Event_ParamN</c>/<c>Reward_ParamN</c> slot (0-based schema position).</summary>
public sealed record StoryParamSlot(int Position, string RawValue, StorySourceRange Range);

/// <summary>
///     One story event block. A single class regardless of event/reward type - which slots mean
///     what is the schema's business (<c>StoryEventType</c>/<c>StoryRewardType</c> enums), not a
///     type hierarchy's. <see cref="Tags" /> carries every child element in document order;
///     the semantic fields are parsed projections of the well-known tags.
/// </summary>
public sealed record StoryEvent
{
    public required string Name { get; init; }
    public required StorySourceRange NameRange { get; init; }

    /// <summary>Span of the whole <c>&lt;Event&gt;…&lt;/Event&gt;</c> element.</summary>
    public required StorySourceRange Range { get; init; }

    public string? EventType { get; init; }
    public string? EventFilter { get; init; }
    public IReadOnlyList<StoryParamSlot> EventParams { get; init; } = [];

    public string? RewardType { get; init; }
    public IReadOnlyList<StoryParamSlot> RewardParams { get; init; } = [];

    /// <summary>OR of AND-lines: the event arms when any one group's events have all fired.</summary>
    public IReadOnlyList<StoryPrereqGroup> PrereqGroups { get; init; } = [];

    public string? Branch { get; init; }
    public bool Perpetual { get; init; }

    public string? StoryDialog { get; init; }
    public int? StoryChapter { get; init; }

    /// <summary>Every child element in document order (original casing, trimmed values).</summary>
    public IReadOnlyList<StoryEventTag> Tags { get; init; } = [];
}

/// <summary>A structural defect found while parsing a story thread.</summary>
public sealed record StoryParseProblem(StorySourceRange Range, string Message);

/// <summary>The parsed shape of one story thread file.</summary>
public sealed record StoryThread(
    string DocumentUri,
    IReadOnlyList<StoryEvent> Events,
    IReadOnlyList<StoryParseProblem> Problems);