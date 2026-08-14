// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;

namespace PG.StarWarsGame.LSP.Assets.Icons;

/// <summary>Where a resolved icon's pixels came from.</summary>
public enum IconSource
{
    /// <summary>The workspace's own packed mega texture - what the game actually reads.</summary>
    WorkspaceMegaTexture,

    /// <summary>A raw source image in one of the project's configured icon folders.</summary>
    LooseSource,

    /// <summary>The base game's icons, baked into the baseline sidecar.</summary>
    Baseline
}

/// <summary>A resolved icon, and the story of how it resolved.</summary>
/// <param name="Png">PNG-encoded pixels.</param>
/// <param name="Source">Which layer supplied them.</param>
/// <param name="IsMegaTextureStale">
///     True when the workspace ships a mega texture that does NOT contain this icon while a raw
///     source for it does exist - i.e. the icon was drawn but never repacked. The preview still
///     renders (showing the author what they drew) but the game would not, which is precisely the
///     discrepancy worth warning about.
/// </param>
public sealed record IconResolution(byte[] Png, IconSource Source, bool IsMegaTextureStale = false)
{
    private readonly (int Width, int Height) _size =
        PngHeader.TryRead(Png, out var width, out var height) ? (width, height) : default;

    /// <summary>
    ///     The icon's natural width in pixels, or 0 when <see cref="Png" /> could not be read.
    /// </summary>
    /// <remarks>
    ///     Read from the PNG header rather than carried alongside the bytes, so it works the same
    ///     for every layer without changing how any of them are stored - notably without a baseline
    ///     sidecar format bump. Callers need it because the card sizes artwork by its natural
    ///     dimensions; a mod reskinning the chrome at a different size renders wrong otherwise.
    ///     Zero means "unknown, use a nominal size" - never a reason to fail the request.
    /// </remarks>
    public int Width => _size.Width;

    /// <inheritdoc cref="Width" />
    public int Height => _size.Height;
}

/// <summary>
///     Resolves an icon name across the layers a project can supply icons from.
/// </summary>
/// <remarks>
///     <para>
///         Order, and the reasoning behind it:
///     </para>
///     <list type="number">
///         <item>
///             The workspace's own mega texture, when it ships one. A .mtd is NOT mergeable - a mod
///             that ships one has to contain the base game's entries plus its own - so its presence
///             REPLACES the baked baseline set wholesale rather than layering on top of it.
///         </item>
///         <item>
///             A raw source image. Reached only when the mega texture does not carry the name, which
///             means either the pack is stale or there is no pack at all.
///         </item>
///         <item>
///             The baked baseline - but ONLY when the workspace ships no mega texture of its own.
///             Falling back to it otherwise would paper over a genuinely broken mod: the game would
///             show nothing, so neither should the preview.
///         </item>
///     </list>
///     <para>
///         Nothing here decodes files; a caller supplies raw sources already converted to PNG, so
///         this stays a pure decision table over three dictionaries.
///     </para>
/// </remarks>
public sealed class IconCatalog
{
    private readonly ImmutableDictionary<string, byte[]> _baseline;
    private readonly ImmutableDictionary<string, byte[]> _loose;
    private readonly ImmutableDictionary<string, byte[]>? _workspace;

    /// <param name="workspaceMegaTexture">
    ///     Icons from the workspace's own mega texture, keyed as the .mtd records them (with the
    ///     <c>.TGA</c> suffix). <see langword="null" /> - not empty - when the workspace ships no
    ///     mega texture at all; the distinction drives whether the baseline is consulted.
    /// </param>
    /// <param name="looseSources">Raw source images, keyed by base name without extension.</param>
    /// <param name="baseline">The base game's baked icons, keyed as the .mtd records them.</param>
    public IconCatalog(
        IReadOnlyDictionary<string, byte[]>? workspaceMegaTexture,
        IReadOnlyDictionary<string, byte[]> looseSources,
        IReadOnlyDictionary<string, byte[]> baseline)
    {
        ArgumentNullException.ThrowIfNull(looseSources);
        ArgumentNullException.ThrowIfNull(baseline);

        _workspace = workspaceMegaTexture?.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
        _loose = looseSources.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
        _baseline = baseline.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether the workspace ships its own mega texture.</summary>
    public bool HasWorkspaceMegaTexture => _workspace is not null;

    /// <summary>
    ///     Base names that exist as raw sources but are absent from the workspace's mega texture -
    ///     drawn, but never repacked.
    /// </summary>
    /// <remarks>
    ///     The backing set for the "your mega texture is out of sync, rebuild it" warning, which is a
    ///     usefully different message from "icon not found": the art exists and the fix is a repack,
    ///     not a drawing. Always empty when the workspace ships no mega texture, since there is then
    ///     nothing for the sources to be out of sync with.
    /// </remarks>
    public IReadOnlySet<string> IconsAwaitingRepack
    {
        get
        {
            if (_workspace is null)
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return _loose.Keys
                .Where(name => !_workspace.ContainsKey(WithTgaSuffix(name)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    ///     Resolves <paramref name="iconName" />, which may be written with or without the
    ///     <c>.TGA</c> suffix - XML authors write either, and a .mtd always records the suffixed
    ///     form. Returns <see langword="null" /> when no layer supplies it.
    /// </summary>
    public IconResolution? Resolve(string iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName))
            return null;

        var suffixed = WithTgaSuffix(iconName);
        var bare = WithoutExtension(iconName);

        if (_workspace is not null)
        {
            if (_workspace.TryGetValue(suffixed, out var packed))
                return new IconResolution(packed, IconSource.WorkspaceMegaTexture);

            // Drawn but not repacked: render it so the author can see their work, and flag the
            // discrepancy so they learn the game would not show it yet.
            return _loose.TryGetValue(bare, out var stale)
                ? new IconResolution(stale, IconSource.LooseSource, IsMegaTextureStale: true)
                : null;
        }

        // No workspace mega texture: the project's own raw art still outranks the base game's.
        if (_loose.TryGetValue(bare, out var loose))
            return new IconResolution(loose, IconSource.LooseSource);

        return _baseline.TryGetValue(suffixed, out var baseline)
            ? new IconResolution(baseline, IconSource.Baseline)
            : null;
    }

    private static string WithTgaSuffix(string name)
    {
        var trimmed = name.Trim();
        return trimmed.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + ".TGA";
    }

    private static string WithoutExtension(string name)
    {
        var trimmed = name.Trim();
        var dot = trimmed.LastIndexOf('.');
        return dot > 0 ? trimmed[..dot] : trimmed;
    }
}
