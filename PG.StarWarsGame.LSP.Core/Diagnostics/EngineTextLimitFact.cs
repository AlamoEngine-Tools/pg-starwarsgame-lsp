// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: text longer than the fixed buffer the engine copies it into.
/// </summary>
/// <remarks>
///     <para>
///         The engine reads XML values in place, so nothing is bounded until a value is handed to
///         the database mapper - which copies it into a stack buffer with <c>strcpy</c> and no
///         length. The size check next to that copy is an <c>assert</c>, so it exists in the build
///         Petroglyph tested with and NOT in the build anyone plays. There is no truncation and no
///         error message: the copy simply runs off the end of the buffer.
///     </para>
///     <para>
///         Measured limits, each read from the comparison immediately before its <c>strcpy</c>:
///         a tag VALUE gets 8192 bytes (<c>DatabaseMap.cpp</c> line 5582, <c>CMP EAX, 0x2000</c>),
///         and an object NAME gets 128 (<c>GameObjectType.cpp</c> line 2225). Both are exclusive -
///         the check is <c>size() &lt; limit</c>.
///     </para>
/// </remarks>
/// <param name="Subject">What was too long, for the message - a tag name or the object's name.</param>
/// <param name="CharactersOver">
///     How many characters must come off the end before the engine will take it, or 0 when it
///     already fits. THE number the author acts on - deliberately not the byte overage, which is a
///     different and larger figure whenever the text is not plain ASCII.
/// </param>
/// <param name="CharactersLeft">
///     How many more plain characters fit, or 0 when it is already over.
/// </param>
/// <param name="ByteCount">
///     What the engine actually counts, kept for the record and for re-measuring against a later
///     build. It has no place in the message: bytes are the engine's unit, and nobody editing XML
///     thinks in them.
/// </param>
/// <param name="Limit">The buffer size in bytes; the last legal length is one less.</param>
public sealed record EngineTextLimitFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string Subject,
    int CharactersOver,
    int CharactersLeft,
    int ByteCount,
    int Limit) : XmlFact(DocumentUri, Line, Column, Length);
