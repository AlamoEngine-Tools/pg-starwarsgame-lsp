// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Configuration;

public record LspConfiguration
{
    public string? GamePath { get; init; }
    public IReadOnlyList<string> ModPaths { get; init; } = [];
    public string Locale { get; init; } = "en";
    public SchemaSourceConfig SchemaSource { get; init; } = new();
    public BaselineSourceConfig BaselineSource { get; init; } = new();
    public IReadOnlyList<string> XmlDirectories { get; init; } = [];
}