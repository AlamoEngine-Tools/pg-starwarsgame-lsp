// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Configuration;

public record LspConfiguration
{
    public string? WorkspaceRoot { get; init; }
    public string? GamePath { get; init; }
    public string? ExpansionPath { get; init; }

    /// <summary>
    ///     Where the user's copy of the base game's shader SOURCES lives.
    /// </summary>
    /// <remarks>
    ///     Not part of a game install: the install ships <c>.fxo</c>, compiled DX9 bytecode, which is
    ///     no use to a translator. The sources are published separately by Petroglyph and are never
    ///     redistributed with this extension, so this points at wherever the user put their own copy.
    /// </remarks>
    public string? ShaderPath { get; init; }
    public string Locale { get; init; } = "en";
    public SchemaSourceConfig SchemaSource { get; init; } = new();
    public BaselineSourceConfig BaselineSource { get; init; } = new();
    public LocalisationConfig Localisation { get; init; } = new();
    public FeatureFlags Features { get; init; } = new();
}