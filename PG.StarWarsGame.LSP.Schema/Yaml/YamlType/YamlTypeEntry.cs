// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Yaml.YamlType;

/// <summary>YAML deserialization model for a single object-type entry.</summary>
internal sealed class YamlTypeEntry
{
    public string TypeName { get; set; } = string.Empty;
    public string? NameTag { get; set; }
    public Dictionary<string, string> Description { get; set; } = [];
}