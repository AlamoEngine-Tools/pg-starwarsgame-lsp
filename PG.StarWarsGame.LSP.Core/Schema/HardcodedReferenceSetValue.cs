// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

public sealed record HardcodedReferenceSetValue
{
    public required string Name { get; init; }
    public IReadOnlyDictionary<string, string> Description { get; init; } = new Dictionary<string, string>();
    public bool Deprecated { get; init; }
    public string? AvailableSince { get; init; }

    /// <summary>
    ///     Zero or more value-group keys this value belongs to. Empty means "all groups."
    ///     Used to filter valid values when the enclosing tag carries a <c>valueGroup</c> annotation.
    /// </summary>
    public IReadOnlyList<string> Groups { get; init; } = [];
}
