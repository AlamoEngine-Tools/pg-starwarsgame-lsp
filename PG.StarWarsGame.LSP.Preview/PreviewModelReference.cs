// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Preview;

/// <summary>
///     Turns whatever the client sends into the bare model name the resolver expects.
/// </summary>
/// <remarks>
///     Two callers send two shapes: the XML writes <c>EV_StarDestroyer.ALO</c>, while a custom editor
///     opening a file sends a <c>file:///</c> URI. Both mean the same model, and normalising here
///     keeps that knowledge out of every handler.
/// </remarks>
public static class PreviewModelReference
{
    private const string ModelDirectory = "Data/Art/Models/";

    public static string Normalise(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return string.Empty;

        var value = reference.Trim();

        if (value.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            value = Uri.UnescapeDataString(value);

        // Take the file name whichever separator was used - a URI brings forward slashes, the XML
        // sometimes brings backslashes, and neither carries a path we should trust anyway.
        var lastSlash = value.LastIndexOfAny(['/', '\\']);
        return lastSlash >= 0 ? value[(lastSlash + 1)..] : value;
    }

    /// <summary>The game-relative path a model or animation reference resolves against.</summary>
    public static string ModelPath(string reference)
    {
        return ModelDirectory + Normalise(reference);
    }

    /// <summary>
    ///     Removes the <c>_ALT&lt;n&gt;</c> and <c>_LOD&lt;n&gt;</c> tags a PROXY name carries, leaving
    ///     the asset name the effect is actually filed under.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A proxy carries its damage stage and detail level in its own name, and the particle
    ///         system it names is filed without them: <c>p_smoke_small_thin_ALT2</c> is a proxy,
    ///         <c>p_smoke_small_thin.alo</c> is the file. The engine strips the tags before it loads the
    ///         particle system, so without this every level-tagged effect asks for a file that exists
    ///         nowhere. 152 shipped models carry 206 such names between them.
    ///     </para>
    ///     <para>
    ///         Every occurrence, either marker, in either order: <c>p_fire_small01_ALT3_LOD0</c> is a
    ///         real shape. A marker with no digits after it is part of the name and is left alone,
    ///         which is the rule the reader uses to decide there is no level there at all.
    ///     </para>
    ///     <para>
    ///         Only ever applied to a proxy name. No shipped asset in either tree has a level tag in
    ///         its real file name, so this cannot take a name away from a file that has one - but a
    ///         model reference out of the XML is an authored file name and has no business here.
    ///     </para>
    /// </remarks>
    public static string StripLevelSuffix(string? reference)
    {
        return ModelLevelTag.Strip(reference);
    }
}