// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     What is wrong with an XML document's structure, judged against the game's own XML reader
///     and against standard XML. Each category has its own id in the XML strictness group.
/// </summary>
public enum XmlStrictnessCategory
{
    // ── the game drops the whole file ──
    MissingDeclaration,
    EndTagMismatch,
    EndTagCaseMismatch,
    StrayEndTag,
    EmptyRoot,
    MultipleRoots,
    AttributeSyntax,
    CommentSyntax,
    UnexpectedEndOfFile,

    // ── the game reads the file; standard XML tools reject it ──
    MalformedDeclaration,
    StrayAmpersand,
    StrictOnly,

    // ── the game reads the file, but not everything the document shows ──
    CharacterDataAfterChild,
    UntrimmedValueCharacter,

    // ── the game reads it as shown; other tools may read a different value ──
    CommentInsideValue
}
