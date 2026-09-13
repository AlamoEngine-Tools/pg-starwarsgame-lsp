// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

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

    /// <summary>Bytes, as the engine counts them - not characters. UTF-8, matching how the files are read.</summary>
    public static int ByteCount(string text)
    {
        return System.Text.Encoding.UTF8.GetByteCount(text);
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
