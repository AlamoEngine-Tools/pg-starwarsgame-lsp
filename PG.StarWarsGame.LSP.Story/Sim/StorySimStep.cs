// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Story.Graph;

namespace PG.StarWarsGame.LSP.Story.Sim;

/// <summary>
///     One transition in the simulation trace. <see cref="From" /> and <see cref="To" /> are the
///     event's lifecycle before and after; both are null for a step that is not a lifecycle change
///     (a flag write, an owed completion, an ignored reward). <see cref="SourceNodeId" /> is the
///     event whose firing caused this one - the edge the client animates runs from it to
///     <see cref="NodeId" />. <see cref="Seq" /> is the position in the whole trace, so a client can
///     ask for everything after the last step it has.
/// </summary>
public sealed record StorySimStep(
    int Tick,
    int Seq,
    string NodeId,
    StoryEventLifecycle? From,
    StoryEventLifecycle? To,
    string? SourceNodeId,
    string Cause,
    string? Detail = null);

/// <summary>The causes a step can carry. Strings, so they travel the wire as they are.</summary>
public static class StorySimCause
{
    /// <summary>Armed at load because the event has no prereq line.</summary>
    public const string Load = "load";

    /// <summary>Fired by the user declaring its trigger condition met.</summary>
    public const string Manual = "manual";

    /// <summary>Fired by a simulated Lua <c>Story_Event</c> call.</summary>
    public const string Lua = "lua";

    /// <summary>Fired by the per-tick poll (STORY_ELAPSED, STORY_FLAG).</summary>
    public const string Poll = "poll";

    /// <summary>Armed, or fired as a STORY_TRIGGER, by a prereq's push.</summary>
    public const string Prereq = "prereq";

    /// <summary>Fired by a TRIGGER_EVENT reward (or RESET_BRANCH's second parameter).</summary>
    public const string Trigger = "trigger";

    /// <summary>Fired by a world change the author made (or a reward's write to the world).</summary>
    public const string World = "world";

    /// <summary>A fact written to the world (detail says which).</summary>
    public const string Fact = "fact";

    /// <summary>Cleared by RESET_EVENT or RESET_BRANCH.</summary>
    public const string Reset = "reset";

    public const string Disable = "disable";
    public const string Enable = "enable";

    /// <summary>A perpetual event re-armed after firing.</summary>
    public const string Perpetual = "perpetual";

    /// <summary>A STORY_SPEECH_DONE fired by the owed speech completion.</summary>
    public const string Speech = "speech";

    /// <summary>A STORY_MOVIE_DONE fired by the owed movie completion.</summary>
    public const string Movie = "movie";

    /// <summary>A suspended thread activated by STORY_ELEMENT (detail = thread name).</summary>
    public const string Thread = "thread";

    /// <summary>A flag written by a reward (detail = "NAME=value").</summary>
    public const string Flag = "flag";

    /// <summary>A reward or command the engine would ignore (detail says why).</summary>
    public const string Ignored = "ignored";

    /// <summary>The run halted after this tick because the node hit a breakpoint.</summary>
    public const string Breakpoint = "breakpoint";

    /// <summary>The causes that mean "this event fired", for fire counts.</summary>
    public static readonly IReadOnlySet<string> Fires =
        new HashSet<string>(StringComparer.Ordinal) { Manual, Lua, Poll, Prereq, Trigger, Speech, Movie, World };
}

/// <summary>
///     Where a run pauses. <see cref="NodeIds" /> halt after the tick in which that event fires;
///     <see cref="OnConditionalGates" /> halts after any tick in which a clock or flag gated event
///     fires. A halt is a pause: the next command continues past it.
/// </summary>
public sealed record StorySimBreakpoints(ImmutableHashSet<string> NodeIds, bool OnConditionalGates)
{
    public static readonly StorySimBreakpoints None = new(ImmutableHashSet<string>.Empty, false);
}