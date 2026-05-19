// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Yaml;

/// <summary>Deserialized form of _index.json from the schema repository.</summary>
public sealed class SchemaManifest
{
    public List<string> Tags { get; set; } = [];
    public List<string> Types { get; set; } = [];
    public List<string> Enums { get; set; } = [];
    public List<string> Hardcoded { get; set; } = [];
    public List<string> Meta { get; set; } = [];
}