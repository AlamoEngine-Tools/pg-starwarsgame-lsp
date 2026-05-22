// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Yaml;

/// <summary>YAML deserialization model for a single positional parameter slot on an event/reward enum value.</summary>
internal sealed class YamlParamEntry
{
    /// <summary>0-based slot index. Event_Param1 = position 0, Reward_Param1 = position 0.</summary>
    public int Position { get; set; }

    public string Type { get; set; } = string.Empty;
    public string? ReferenceKind { get; set; }
    public string? ReferenceType { get; set; }
    public string? EnumName { get; set; }
    public bool Optional { get; set; }
    public Dictionary<string, string> Description { get; set; } = [];
    public Dictionary<string, string> Notes { get; set; } = [];
}