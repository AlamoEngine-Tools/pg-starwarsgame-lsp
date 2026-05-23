// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Schema.Yaml;

/// <summary>
///     Pre-resolution enum definition. Values use <see cref="RawEnumValueDefinition" /> with unresolved param
///     references.
/// </summary>
internal sealed record RawEnumDefinition
{
    public required string Name { get; init; }
    public EnumKind Kind { get; init; }
    public bool IsBitfield { get; init; }
    public string? SourceFile { get; init; }
    public IReadOnlyDictionary<string, string> Description { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Notes { get; init; } = new Dictionary<string, string>();
    public bool Deprecated { get; init; }
    public string? AvailableSince { get; init; }
    public required IReadOnlyList<RawEnumValueDefinition> Values { get; init; }
}