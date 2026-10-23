// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Configuration;

/// <summary>
///     The experimental features, one switch each. Everything else is always on. Every flag defaults
///     to <c>true</c>, so a bare server (no client-supplied <c>features</c> node) runs everything;
///     the user-facing defaults - off - live in the VS Code client's package.json, and the client
///     always sends the resolved object via initializationOptions.
///     In <c>.pg-lsp.json</c> the node is spelled PascalCase (<c>"Features"</c>, case-sensitive);
///     in initializationOptions it is camelCase (<c>"features"</c>, parsed case-insensitively).
/// </summary>
public record FeatureFlags
{
    /// <summary>
    ///     Edit mode in the story graph: the authoring endpoints that stage, preview, validate and
    ///     write story changes.
    /// </summary>
    public bool StoryEditing { get; init; } = true;

    /// <summary>
    ///     The story simulator: every <c>aet/storySim*</c> endpoint and the
    ///     <c>aet/storySimChanged</c> notification.
    /// </summary>
    public bool StorySimulator { get; init; } = true;
}