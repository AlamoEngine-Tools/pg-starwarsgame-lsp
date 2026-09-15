// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Fixed buffer sizes the engine copies XML text into, measured from the 2018 build.
/// </summary>
/// <remarks>
///     <para>
///         Each was read from the comparison immediately before an unbounded <c>strcpy</c>, not from
///         a struct definition - the instruction is the specification. Each check is exclusive
///         (<c>size() &lt; limit</c>), and each is an <c>assert</c>, which means it is present in
///         the build Petroglyph tested with and absent from the one anyone plays.
///     </para>
///     <para>
///         These are engine facts, so they live beside the diagnostic that reports them rather than
///         in the schema: they are true of every tag regardless of what the schema says a tag holds,
///         and they will need re-measuring against the 64-bit build rather than versioning.
///     </para>
/// </remarks>
public static class EngineTextLimits
{
    /// <summary>
    ///     A tag's value, copied into <c>strtok_string_buffer</c> by
    ///     <c>DatabaseMapClass::Map_Data_Of_Type</c> - <c>DatabaseMap.cpp</c> line 5582,
    ///     <c>CMP EAX, 0x2000</c>.
    /// </summary>
    /// <remarks>
    ///     Applies only to the value types listed in <see cref="CopiedToBuffer" /> - see
    ///     <see cref="ValueIsCopiedToBuffer" />.
    /// </remarks>
    public const int TagValue = 0x2000;

    /// <summary>
    ///     A tag's NAME, uppercased into <c>uppercase_key_name</c> by
    ///     <c>DatabaseMapClass::Map_DB_Data_To_Class</c> - <c>DatabaseMap.cpp</c> line 4563,
    ///     <c>CMP EAX, 0x100</c>. Not reported today: a name that long resolves to no tag at all, so
    ///     the unknown-tag rule reaches it first and says something more useful.
    /// </summary>
    public const int TagName = 0x100;

    /// <summary>
    ///     An object's name, copied and uppercased for hashing by
    ///     <c>GameObjectTypeClass::Get_Name_CRC</c> - <c>GameObjectType.cpp</c> line 2225.
    /// </summary>
    public const int ObjectName = 128;

    /// <summary>
    ///     The fraction of a limit at which it is worth warning, before anything is wrong.
    /// </summary>
    /// <remarks>
    ///     These limits are reached by ACCUMULATION - another hardpoint, another planet, a rename
    ///     from <c>HP01</c> to something readable. By the time the limit is crossed the change that
    ///     crossed it is rarely the change that caused it, so the useful moment to say something is
    ///     while there is still room to act.
    /// </remarks>
    public const double WarnAtFraction = 0.9;

    /// <summary>
    ///     The engine type codes whose case in <c>Map_Data_Of_Type</c> copies the value into
    ///     <c>strtok_string_buffer</c>, and which the <see cref="TagValue" /> limit therefore binds.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>Map_Data_Of_Type</c> is one switch on the type code -
    ///         <c>CMP 0x52; JA default; JMP [ECX*4 + 0xcbbed4]</c>, 83 entries - so the copy belongs
    ///         to individual CASES and not to the function. Mapping all 50
    ///         <c>strtok_string_buffer</c> assert sites through that jump table gives these codes:
    ///         50 of the 83, and every one of them a composite. The 33 without it are the scalars
    ///         and single references, read straight out of the node and never copied.
    ///     </para>
    ///     <para>
    ///         Kept as CODES rather than as <see cref="XmlValueType" /> names on purpose.
    ///         <c>(int)XmlValueType</c> is the engine type code, so this is the measurement itself
    ///         rather than a transcription of it, and it can be re-derived against another build by
    ///         re-reading the jump table - see <c>tools/map_type_code_cases.py</c> in the decompile
    ///         repository.
    ///     </para>
    /// </remarks>
    private static readonly HashSet<int> CopiedToBuffer =
    [
        0x0f, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x18, 0x19,
        0x1a, 0x1b, 0x1e, 0x22, 0x23, 0x24, 0x25, 0x26, 0x28, 0x29,
        0x2a, 0x2b, 0x2d, 0x2e, 0x2f, 0x30, 0x31, 0x34, 0x35, 0x36,
        0x37, 0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40, 0x41, 0x42, 0x44,
        0x45, 0x46, 0x47, 0x48, 0x4c, 0x4d, 0x4e, 0x4f, 0x51, 0x52
    ];

    /// <summary>
    ///     Whether a value of this type is copied into the fixed buffer, and so whether
    ///     <see cref="TagValue" /> applies to it at all.
    /// </summary>
    public static bool ValueIsCopiedToBuffer(XmlValueType type)
    {
        return CopiedToBuffer.Contains((int)type);
    }

    /// <summary>Bytes, as the engine counts them - not characters. UTF-8, matching how the files are read.</summary>
    public static int ByteCount(string text)
    {
        return Encoding.UTF8.GetByteCount(text);
    }

    /// <summary>
    ///     How many characters have to come off the end before the engine will take this text.
    /// </summary>
    /// <remarks>
    ///     Not the byte overage relabelled. The engine counts bytes, but an author deletes
    ///     CHARACTERS, and a multi-byte one takes several bytes with it - 809 bytes over is 270
    ///     characters when the text is three-byte, and telling someone to remove 809 would have them
    ///     delete three times what they need to.
    /// </remarks>
    public static int CharactersToRemove(string text, int limit)
    {
        var mustShed = ByteCount(text) - (limit - 1);
        if (mustShed <= 0) return 0;

        var runes = text.EnumerateRunes().ToArray();
        var shed = 0;
        var characters = 0;
        for (var i = runes.Length - 1; i >= 0 && shed < mustShed; i--)
        {
            shed += runes[i].Utf8SequenceLength;
            // A rune outside the BMP is two chars in the file, and deleting it removes both.
            characters += runes[i].Utf16SequenceLength;
        }

        return characters;
    }

    /// <summary>
    ///     How many more plain characters fit. Exact for the ASCII that names and lists are made of,
    ///     which is the only thing anyone adds to a value that is already near the limit.
    /// </summary>
    public static int CharactersLeft(string text, int limit)
    {
        return Math.Max(0, limit - 1 - ByteCount(text));
    }
}