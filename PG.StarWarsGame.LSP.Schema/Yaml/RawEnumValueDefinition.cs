//  Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Yaml;

/// <summary>Pre-resolution enum value definition. Params are unresolved <see cref="RawParamDefinition" /> entries.</summary>
internal sealed record RawEnumValueDefinition
{
    public required string Name { get; init; }
    public IReadOnlyDictionary<string, string> Description { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Notes { get; init; } = new Dictionary<string, string>();
    public bool Deprecated { get; init; }

    /// <summary>Documented but never engine-verified - consumers surface a warning instead of trusting it.</summary>
    public bool Untested { get; init; }

    public string? AvailableSince { get; init; }
    public IReadOnlyList<string> Groups { get; init; } = [];
    public IReadOnlyList<RawParamDefinition>? Params { get; init; }
}