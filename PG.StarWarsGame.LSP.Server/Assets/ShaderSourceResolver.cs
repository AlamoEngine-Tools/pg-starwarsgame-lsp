// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Microsoft.Extensions.Logging;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Util;

namespace PG.StarWarsGame.LSP.Server.Assets;

/// <summary>
///     Finds a shader's source text.
/// </summary>
/// <remarks>
///     <para>
///         Three tiers, in order: the mod's own <c>.fx</c> files, then the managed copy of the base
///         game's, then nothing. A mod that ships a replacement shader has to win, or previewing the
///         mod would show the stock look; and "nothing" is a legitimate answer, because the renderer
///         keeps archetype materials for exactly that case.
///     </para>
///     <para>
///         <strong>The base shaders are never shipped with this extension.</strong> They are
///         Petroglyph's, marked confidential in their own headers, and this repository is public. What
///         ships is the translator; the sources are the user's own copy, fetched from Petroglyph's
///         published mod-tooling download, and the managed directory is wherever that copy was put.
///     </para>
/// </remarks>
public sealed class ShaderSourceResolver(
    IFileHelper fileHelper,
    ILspConfigurationProvider config,
    IGameAssetResolver assets,
    ILogger<ShaderSourceResolver> logger)
{
    /// <summary>Where a mod keeps its shaders, relative to the game root.</summary>
    private const string ShaderDirectory = "Data/Art/Shaders/";

    /// <summary>
    ///     The two shader extensions.
    /// </summary>
    /// <remarks>
    ///     An allow-list, not a convenience: the name comes from the client, and without this the
    ///     endpoint would read any file in the managed directory.
    /// </remarks>
    private static readonly string[] ShaderExtensions = [".fx", ".fxh"];

    /// <summary>
    ///     Whether a managed copy of the base shaders is actually reachable.
    /// </summary>
    /// <remarks>
    ///     Reported so the UI can explain a plain-looking preview instead of leaving the author to
    ///     wonder whether something is broken.
    /// </remarks>
    public bool HasManagedShaders
    {
        get
        {
            var directory = ManagedDirectory();
            return directory is not null
                   && fileHelper.FileSystem.Directory.EnumerateFiles(
                       directory, "*.fx", SearchOption.AllDirectories).Any();
        }
    }

    /// <summary>The shader's text, or null when no tier has it.</summary>
    public string? Read(string shaderName)
    {
        if (!IsBareShaderName(shaderName))
        {
            logger.LogDebug("Refused shader name {Name}", shaderName);
            return null;
        }

        // The mod's own, through the normal asset layering - so a dependency's shader loses to the
        // root project's, the same way every other asset does.
        var fromWorkspace = assets.Read(ShaderDirectory + shaderName);
        if (fromWorkspace is not null)
            return Decode(fromWorkspace);

        var directory = ManagedDirectory();
        if (directory is null)
            return null;

        foreach (var candidate in ManagedCandidates(directory, shaderName))
            if (fileHelper.FileSystem.File.Exists(candidate))
                try
                {
                    return fileHelper.FileSystem.File.ReadAllText(candidate);
                }
                catch (Exception e)
                {
                    // One unreadable shader must not take the whole preview down with it.
                    logger.LogWarning(e, "Could not read shader {Path}", candidate);
                    return null;
                }

        return null;
    }

    /// <summary>
    ///     Where the shader may sit under the managed directory.
    /// </summary>
    /// <remarks>
    ///     Both forms, because the published archive contains a <c>Shaders/</c> folder and people
    ///     extract it as-is about as often as they extract its contents.
    /// </remarks>
    private static IEnumerable<string> ManagedCandidates(string directory, string shaderName)
    {
        yield return Path.Combine(directory, shaderName);
        yield return Path.Combine(directory, "Shaders", shaderName);
    }

    private string? ManagedDirectory()
    {
        var configured = config.Current.ShaderPath;

        return string.IsNullOrWhiteSpace(configured)
               || !fileHelper.FileSystem.Directory.Exists(configured)
            ? null
            : configured;
    }

    /// <summary>
    ///     Whether this is a bare shader file name and nothing else.
    /// </summary>
    /// <remarks>
    ///     Refused rather than sanitised. The name arrives over the wire, and there is no legitimate
    ///     request this rejects - a shader is always referenced by name alone - so trimming a path
    ///     into shape would only create somewhere for a mistake to hide.
    /// </remarks>
    private static bool IsBareShaderName(string shaderName)
    {
        if (string.IsNullOrWhiteSpace(shaderName))
            return false;

        if (shaderName.Contains('/') || shaderName.Contains('\\') || shaderName.Contains(".."))
            return false;

        if (Path.IsPathRooted(shaderName) || Path.GetFileName(shaderName) != shaderName)
            return false;

        return ShaderExtensions.Contains(
            Path.GetExtension(shaderName), StringComparer.OrdinalIgnoreCase);
    }

    private static string Decode(byte[] bytes)
    {
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
