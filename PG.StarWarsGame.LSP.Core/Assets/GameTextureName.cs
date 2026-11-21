// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Assets;

/// <summary>
///     How the game turns the texture name a model or particle system carries into the file it
///     opens.
/// </summary>
/// <remarks>
///     <para>
///         MEASURED on the 2024 64-bit build's texture loader: the name is tried as written, then the
///         part after its LAST BACKSLASH is tried under each texture directory; when nothing is found
///         the same is repeated with <c>.dds</c> in place of the extension. A forward slash is not a
///         cut point there.
///     </para>
///     <para>
///         Measured in the game (FoC x64, 2024 release, #420): the name is opened exactly as written,
///         whatever its extension; when that file does not exist, the name up to its FIRST <c>.</c> with
///         <c>.dds</c> appended is opened instead, and nothing else is tried - a missing <c>.dds</c> never
///         reaches the <c>.tga</c> of its stem. The file found goes to the Direct3D loader as raw bytes
///         with no format stated, so its CONTENT decides how it is decoded: a PNG loads like a TGA or a
///         DDS, under any extension. <see cref="Candidates" /> is that rule.
///     </para>
///     <para>
///         Why it matters: the shipped art names its sprite sheets by the art machine's absolute path
///         in 37 particle files per game - three names in all, such as
///         <c>C:\Projects\Starwars\Art\Textures\Game\p_particle_master.tga</c> - and they render in
///         game because of this rule. Looked up as written they were reported missing everywhere
///         (#292). Every place that resolves a texture name goes through here, so the asset resolver, the
///         Mod Browser, the model preview, the particle preview, the hover and the XML checks agree.
///     </para>
/// </remarks>
public static class GameTextureName
{
    /// <summary>The extension the game falls back to when the name as written does not exist.</summary>
    public const string FallbackExtension = ".dds";

    private const string PngExtension = ".png";
    private const string TgaExtension = ".tga";

    /// <summary>
    ///     The texture formats the asset catalog records: DDS, TGA and PNG. The game decodes other image
    ///     formats too (out of scope, #420); they would join here.
    /// </summary>
    public static IReadOnlyList<string> Extensions { get; } = [FallbackExtension, TgaExtension, PngExtension];

    /// <summary>The name the game looks up: what follows the last backslash of <paramref name="named" />, trimmed.</summary>
    public static string FileName(string named)
    {
        var name = named.Trim();
        var cut = name.LastIndexOf('\\');
        return cut < 0 ? name : name[(cut + 1)..];
    }

    /// <summary>
    ///     The names the game opens for <paramref name="named" />, in order: the name as written, then - unless
    ///     that is the same file - the name up to the first <c>.</c> of its file name with <c>.dds</c> appended.
    /// </summary>
    /// <remarks>
    ///     The cut is made inside the file name, after the last separator, so a dot in a directory of a path
    ///     is not a cut point. That much is inferred, not measured: the measured names carry no dotted
    ///     directory.
    /// </remarks>
    public static IReadOnlyList<string> Candidates(string named)
    {
        var fallback = Fallback(named);
        return string.Equals(fallback, named, StringComparison.OrdinalIgnoreCase) ? [named] : [named, fallback];
    }

    /// <summary>Whether <paramref name="name" /> carries one of the <see cref="Extensions" />.</summary>
    public static bool IsTextureFile(string name)
    {
        var extension = Path.GetExtension(name);
        return Extensions.Any(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether the file at <paramref name="path" /> is a PNG by its extension.</summary>
    public static bool IsPng(string path)
    {
        return Path.GetExtension(path).Equals(PngExtension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     For a <c>.dds</c> name, the <c>.tga</c> of the same stem, which the game never opens in its place:
    ///     what a "not found" says exists but is not looked for. Null for any other name.
    /// </summary>
    public static string? UnsearchedTga(string named)
    {
        var extension = Path.GetExtension(named);
        return extension.Equals(FallbackExtension, StringComparison.OrdinalIgnoreCase)
            ? named[..^extension.Length] + TgaExtension
            : null;
    }

    private static string Fallback(string named)
    {
        var start = Math.Max(named.LastIndexOf('/'), named.LastIndexOf('\\')) + 1;
        var dot = named.IndexOf('.', start);
        return (dot < 0 ? named : named[..dot]) + FallbackExtension;
    }
}