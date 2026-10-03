// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Yaml;

/// <summary>One entry of a tag's <c>slots</c> list, as written in the YAML.</summary>
internal sealed class YamlTupleSlot
{
    public string Label { get; set; } = string.Empty;
    public string? ReferenceKind { get; set; }
    public string? ReferenceType { get; set; }
    public string? EnumName { get; set; }
}
