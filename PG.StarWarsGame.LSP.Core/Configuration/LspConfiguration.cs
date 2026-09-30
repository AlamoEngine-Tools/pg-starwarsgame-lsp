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
    public DiagnosticsConfig Diagnostics { get; init; } = new();
    public EncyclopediaConfig Encyclopedia { get; init; } = new();
}

/// <summary>
///     Settings for the encyclopedia popup preview.
/// </summary>
public record EncyclopediaConfig
{
    /// <summary>
    ///     The screen the preview draws the popup for.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The game sizes this popup's glyphs from the screen it runs on, so a preview with no
    ///         screen in mind cannot be faithful to any of them. These name the display the author
    ///         wants the card to match - their own, usually - and 1920x1080 is the common case.
    ///     </para>
    ///     <para>
    ///         Width and height do different jobs and are not interchangeable: the height sets the
    ///         glyph size, the width converts it into the card's units. Only the exact pair
    ///         800x600 also narrows the line budget.
    ///     </para>
    /// </remarks>
    public int ScreenWidth { get; init; } = 1920;

    /// <inheritdoc cref="ScreenWidth" />
    public int ScreenHeight { get; init; } = 1080;
}

/// <summary>
///     How diagnostics are produced for the workspace as a whole, as opposed to for the document
///     being edited.
/// </summary>
public record DiagnosticsConfig
{
    /// <summary>
    ///     Diagnose every indexed file once the startup scan finishes, rather than only the files
    ///     that get opened.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Off by default, and it is expensive. Measured on the base game - 580 files, smaller
    ///         than any real mod: 18,281 diagnostics and 21.8 MiB pushed to the editor over 11
    ///         seconds, on top of a 2 second scan. A mod is larger, and the whole of it lands in the
    ///         problems list before anyone has opened a file.
    ///     </para>
    ///     <para>
    ///         It exists for one case: a start where the editor's open-document notification never
    ///         reaches the server, after which nothing is diagnosed at all until the server is
    ///         restarted. The sweep runs after the startup gate opens, so it delays no request - it
    ///         costs throughput and memory in the editor, not responsiveness in the server.
    ///     </para>
    /// </remarks>
    public bool WorkspaceOnStartup { get; init; }
}