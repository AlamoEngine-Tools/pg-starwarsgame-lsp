// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     What makes a game object a planet, a star base, a squadron. The engine never looks at the
///     element an object is declared with; it tests behaviour bits and a few flags, so a kind is a
///     predicate over the object's effective content. An object is of the kind when EVERY declared
///     group holds, and a group holds when ANY of its entries does.
/// </summary>
/// <remarks>
///     <para>
///         The three groups mirror the three tests the engine has. A behaviour is tested against
///         the bitmap built from the object's <c>&lt;Behavior&gt;</c> list, never from the
///         mode-specific lists, so a token that only appears in <c>&lt;SpaceBehavior&gt;</c> does
///         not make the object that kind. A flag is a boolean tag the engine reads into the type
///         (<c>Is_Named_Hero</c>). Membership is set after load on every object another object
///         lists in one of the named tags (<c>Squadron_Units</c> marks each unit as a squadron
///         member).
///     </para>
/// </remarks>
public sealed record ObjectKindDefinition
{
    public required string Kind { get; init; }

    /// <summary>Behaviour tokens, any of which marks the kind. Compared case-insensitively.</summary>
    public IReadOnlyList<string> Behaviors { get; init; } = [];

    /// <summary>Boolean tags, any of which being true marks the kind.</summary>
    public IReadOnlyList<string> Flags { get; init; } = [];

    /// <summary>List tags of OTHER objects; being listed in any of them marks the kind.</summary>
    public IReadOnlyList<string> MemberOf { get; init; } = [];

    /// <summary>Locale to description text.</summary>
    public IReadOnlyDictionary<string, string> Description { get; init; } = new Dictionary<string, string>();

    /// <summary>Locale to caveat text.</summary>
    public IReadOnlyDictionary<string, string> Notes { get; init; } = new Dictionary<string, string>();

    /// <summary>A kind with no predicate at all matches nothing; the schema test rejects it.</summary>
    public bool HasPredicate => Behaviors.Count > 0 || Flags.Count > 0 || MemberOf.Count > 0;
}
