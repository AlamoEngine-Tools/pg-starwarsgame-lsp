// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

public sealed record EnumValueDefinition
{
    public required string Name { get; init; }
    public IReadOnlyDictionary<string, string> Description { get; init; } = new Dictionary<string, string>();
    public bool Deprecated { get; init; }
    public string? AvailableSince { get; init; }

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
    public IReadOnlyDictionary<string, string> Notes { get; init; } = new Dictionary<string, string>();
}