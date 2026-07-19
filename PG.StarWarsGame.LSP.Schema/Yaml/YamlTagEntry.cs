// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Yaml;

/// <summary>YAML deserialization model for a single tag entry.</summary>
internal sealed class YamlTagEntry
{
    public string Tag { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? ReferenceKind { get; set; }
    public string? ReferenceType { get; set; }
    public string? EnumName { get; set; }
    public string? SemanticType { get; set; }
    public object? ValueGroup { get; set; } // scalar string or YAML sequence - coerced in parser
    public bool Deprecated { get; set; }
    public string? AvailableSince { get; set; }
    public Dictionary<string, string> Description { get; set; } = [];
    public Dictionary<string, string> Notes { get; set; } = [];
    public bool MultipleAllowed { get; set; }
    public string? VariantMode { get; set; }
    public YamlTagValidationOverride? ValidationOverride { get; set; }
}