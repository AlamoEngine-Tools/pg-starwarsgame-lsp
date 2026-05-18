// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

public sealed record HardcodedReferenceSet
{
    public required string Name { get; init; }
    public IReadOnlyList<HardcodedReferenceSetValue> Values { get; init; } = [];
    public IReadOnlyDictionary<string, string> Description { get; init; } = new Dictionary<string, string>();
    public bool Deprecated { get; init; }
    public string? AvailableSince { get; init; }
}
