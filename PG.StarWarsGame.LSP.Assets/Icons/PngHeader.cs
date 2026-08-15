// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;

namespace PG.StarWarsGame.LSP.Assets.Icons;

/// <summary>
///     Reads a PNG's pixel dimensions out of its header.
/// </summary>
/// <remarks>
///     <para>
///         The counterpart to <see cref="PngWriter" />, and deliberately just as small: IHDR is
///         required by the spec to be the FIRST chunk, so width and height sit at fixed offsets 16
///         and 20 and no decoding, inflating or chunk walking is needed to reach them. That makes
///         this valid for any conforming PNG, not only the ones this project writes - which matters,
///         because a project's loose icon sources can arrive as PNGs nobody here encoded.
///     </para>
///     <para>
///         Dimensions are worth the trouble because the card scales artwork by them. A mod that
///         reskins the encyclopedia chrome at a different size renders wrong when the client assumes
///         the base game's, and every layer of the icon catalog stores PNG, so this one reader
///         serves the workspace mega texture, loose sources, the baked baseline and the built-in
///         placeholder alike.
///     </para>
/// </remarks>
public static class PngHeader
{
    /// <summary>Byte offset of IHDR's width field: 8 signature + 4 length + 4 type.</summary>
    private const int WidthOffset = 16;

    private const int HeightOffset = 20;

    /// <summary>Enough bytes to cover both fields.</summary>
    private const int MinimumLength = HeightOffset + 4;

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    ///     Reads <paramref name="png" />'s dimensions, returning <see langword="false" /> when it is
    ///     not a PNG, is too short to hold a header, or declares a non-positive dimension.
    /// </summary>
    /// <remarks>
    ///     Returns a bool rather than throwing because a caller's sensible response is to fall back
    ///     to a nominal size and still draw something, not to fail the whole card over one sprite.
    /// </remarks>
    public static bool TryRead(ReadOnlySpan<byte> png, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (png.Length < MinimumLength || !png[..Signature.Length].SequenceEqual(Signature))
            return false;

        // Both are unsigned per the spec, but the spec also caps them at 2^31-1, so anything with
        // the top bit set is corrupt rather than merely large - reading as signed and rejecting
        // non-positive values covers that and the zero case in one test.
        var declaredWidth = BinaryPrimitives.ReadInt32BigEndian(png[WidthOffset..]);
        var declaredHeight = BinaryPrimitives.ReadInt32BigEndian(png[HeightOffset..]);

        if (declaredWidth <= 0 || declaredHeight <= 0)
            return false;

        width = declaredWidth;
        height = declaredHeight;
        return true;
    }
}
