// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Configuration;

public record LspConfiguration
{
    public string? WorkspaceRoot { get; init; }

    /// <summary>
    ///     Every folder the editor has open, for a multi-root workspace. <see cref="WorkspaceRoot" />
    ///     is the first of them, kept because it is where <c>.pg-lsp.json</c> is looked up first.
    /// </summary>
    public IReadOnlyList<string> WorkspaceRoots { get; init; } = [];
    public string? GamePath { get; init; }
    public string? ExpansionPath { get; init; }
    public string Locale { get; init; } = "en";
    public SchemaSourceConfig SchemaSource { get; init; } = new();
    public BaselineSourceConfig BaselineSource { get; init; } = new();
    public LocalisationConfig Localisation { get; init; } = new();
    public FeatureFlags Features { get; init; } = new();
}