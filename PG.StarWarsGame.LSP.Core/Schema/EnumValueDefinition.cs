// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

public sealed record EnumValueDefinition
{
    /// <summary>The member's key: the name XML writes, which the engine matches ignoring case.</summary>
    public required string Name { get; init; }

    /// <summary>
    ///     What the engine uses for the member where that is not its name - for an animation type, the name
    ///     its files are filed under (<c>&lt;model&gt;_&lt;value&gt;_&lt;NN&gt;.ala</c>). Null: the value is the name.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>The member's value: <see cref="Value" /> where the schema writes one, else its name.</summary>
    public string EngineValue => Value ?? Name;

    public IReadOnlyDictionary<string, string> Description { get; init; } = new Dictionary<string, string>();

    /// <summary>
    ///     Zero or more value-group keys this value belongs to. Empty means "all groups."
    ///     Used by completion providers to pre-filter suggestions when the enclosing tag
    ///     carries a <c>valueGroup</c> annotation.
    /// </summary>
    public IReadOnlyList<string> Groups { get; init; } = [];

    /// <summary>
    ///     Ordered parameter slot definitions for this event/reward value, keyed by position.
    ///     Null = unconstrained (all param positions accept arbitrary input).
    ///     Gaps between positions mean those slots are not used by this event.
    /// </summary>
    public IReadOnlyList<ParamDefinition>? Params { get; init; }

    /// <summary>
    ///     Secondary display text for caveats — "Never used in vanilla", "Disabled in engine", etc.
    ///     Shown alongside description in hover/completion tooltips.
    /// </summary>
    public IReadOnlyList<SchemaNote> Notes { get; init; } = [];
}