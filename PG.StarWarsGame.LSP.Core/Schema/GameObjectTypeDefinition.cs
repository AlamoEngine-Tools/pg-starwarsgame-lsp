// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

public record GameObjectTypeDefinition
{
    public required string TypeName { get; init; }

    /// <summary>XML tag that carries the object's unique name, e.g. "Name". Null for singleton types.</summary>
    public string? NameTag { get; init; }

    /// <summary>Locale → description text.</summary>
    public IReadOnlyDictionary<string, string> Description { get; init; } = new Dictionary<string, string>();

    /// <summary>Locale → secondary caveat text.</summary>
    public IReadOnlyDictionary<string, string> Notes { get; init; } = new Dictionary<string, string>();
}