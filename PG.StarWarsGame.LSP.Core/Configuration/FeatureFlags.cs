// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Configuration;

/// <summary>
///     Per-language, per-capability feature flags. Every flag defaults to <c>true</c>: a bare
///     server (no client-supplied <c>features</c> node) behaves exactly as before flags existed.
///     The user-facing opt-in defaults (Lua hover, Lua diagnostics, localisation tools and story
///     discovery are off out of the box) live in the VS Code client's package.json, which always
///     sends the complete resolved object via initializationOptions.
///     In <c>.pg-lsp.json</c> the node is spelled PascalCase (<c>"Features"</c>, case-sensitive);
///     in initializationOptions it is camelCase (<c>"features"</c>, parsed case-insensitively).
/// </summary>
public record FeatureFlags
{
    public XmlFeatureFlags Xml { get; init; } = new();
    public LuaFeatureFlags Lua { get; init; } = new();
    public ToolsFeatureFlags Tools { get; init; } = new();
    public StoryFeatureFlags Story { get; init; } = new();
    public DialogFeatureFlags Dialog { get; init; } = new();
}

/// <summary>Flags for XML language capabilities.</summary>
public record XmlFeatureFlags
{
    public bool Completion { get; init; } = true;
    public bool Hover { get; init; } = true;
    public bool Diagnostics { get; init; } = true;
    public bool GoToDefinition { get; init; } = true;
    public bool FindReferences { get; init; } = true;
    public bool Rename { get; init; } = true;
    public bool CodeLens { get; init; } = true;
    public bool InlayHints { get; init; } = true;
    public bool CodeActions { get; init; } = true;
    public bool LinkedEditing { get; init; } = true;
}

/// <summary>Flags for Lua language capabilities.</summary>
public record LuaFeatureFlags
{
    public bool Completion { get; init; } = true;
    public bool Hover { get; init; } = true;
    public bool Diagnostics { get; init; } = true;
    public bool GoToDefinition { get; init; } = true;
    public bool Rename { get; init; } = true;
    public bool CodeLens { get; init; } = true;
    public bool InlayHints { get; init; } = true;
    public bool CodeActions { get; init; } = true;
}

/// <summary>Flags for story-mode capabilities.</summary>
public record StoryFeatureFlags
{
    /// <summary>
    ///     Gates the campaign story-chain scan: file typing for plot manifests
    ///     (<c>StoryPlotManifest</c>) and story threads (<c>StoryParser</c>), the chain
    ///     diagnostics, and thereby the story event/reward param validation and completion
    ///     that activate on those file types.
    /// </summary>
    public bool Discovery { get; init; } = true;

    /// <summary>
    ///     Gates the campaign-model graph diagnostics (dangling prereqs, prereq cycles,
    ///     duplicate/ambiguous event names, unreachable events, tag order, flag length).
    /// </summary>
    public bool GraphDiagnostics { get; init; } = true;

    /// <summary>
    ///     Gates story symbol/reference indexing across XML and Lua (event names, flags,
    ///     AI-notification ids) and thereby hover/definition/references on them.
    /// </summary>
    public bool Symbols { get; init; } = true;

    /// <summary>Gates cross-language rename of story symbols (builds on <see cref="Symbols" />).</summary>
    public bool Rename { get; init; } = true;
}

/// <summary>
///     Flags for the story-dialog (.txt) language capabilities — its own family like
///     <see cref="LuaFeatureFlags" />, so completion/hover flags can join later.
/// </summary>
public record DialogFeatureFlags
{
    /// <summary>
    ///     Gates the story-dialog language service: diagnostics for .txt files under the
    ///     pgproj storyDialog directories and the XML-side Story_Dialog/Story_Chapter
    ///     cross-checks.
    /// </summary>
    public bool Diagnostics { get; init; } = true;
}

/// <summary>Flags for cross-language tooling endpoints.</summary>
public record ToolsFeatureFlags
{
    /// <summary>Gates the aet/* localisation endpoints, localisation commands and the create-key code action.</summary>
    public bool Localisation { get; init; } = true;

    /// <summary>Gates aet/getEffectiveObject and the "show effective object" code lens.</summary>
    public bool Variants { get; init; } = true;

    /// <summary>
    ///     Gates the story editor protocol: every <c>aet/getStory*</c> endpoint and the
    ///     <c>aet/storyGraphChanged</c> notification (builds on <c>features.story.discovery</c>).
    /// </summary>
    public bool StoryEditor { get; init; } = true;
}
