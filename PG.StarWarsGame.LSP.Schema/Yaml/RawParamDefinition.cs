// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Schema.Yaml;

/// <summary>Pre-resolution param definition produced by YAML parsing. All cross-schema references remain as strings.</summary>
internal sealed record RawParamDefinition
{
    public required int Position { get; init; }
    public required XmlValueType ValueType { get; init; }
    public ReferenceKind ReferenceKind { get; init; }
    public string? ReferenceType { get; init; }
    public string? EnumName { get; init; }
    public bool Optional { get; init; }
    public IReadOnlyDictionary<string, string> Description { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Notes { get; init; } = new Dictionary<string, string>();
}