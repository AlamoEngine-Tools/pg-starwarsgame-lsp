//  Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Schema.Yaml;

/// <summary>Pre-resolution enum value definition. Params are unresolved <see cref="RawParamDefinition" /> entries.</summary>
internal sealed record RawEnumValueDefinition
{
    public required string Name { get; init; }
    public IReadOnlyDictionary<string, string> Description { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<SchemaNote> Notes { get; init; } = [];
    public IReadOnlyList<string> Groups { get; init; } = [];
    public IReadOnlyList<RawParamDefinition>? Params { get; init; }
}