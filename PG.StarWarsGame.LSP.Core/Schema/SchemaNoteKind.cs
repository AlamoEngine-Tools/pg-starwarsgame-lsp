// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     What a <see cref="SchemaNote" /> says about the element it is attached to.
///     <para>
///         The member's VALUE is its rank: lower is more actionable, and everything that orders
///         notes sorts by it, so a schema author cannot change how loudly the editor speaks by
///         moving two lines. Values are explicit and spaced ten apart so that inserting a kind later
///         cannot renumber the rest.
///     </para>
///     <para>
///         Rank also decides the single answer where only one is possible - the LSP diagnostic tag,
///         one badge in the story graph - so an element that is both bugged and deprecated reads as
///         bugged.
///     </para>
/// </summary>
public enum SchemaNoteKind
{
    /// <summary>
    ///     Accepted by the parser, does not do what it says: the element either does nothing at all
    ///     or does the wrong thing. An author who leaves it in ships something broken, which is why
    ///     this is an error rather than a style note.
    /// </summary>
    BuggedInEngine = 10,

    /// <summary>Worked once, something replaced it. The element still does its job.</summary>
    Deprecated = 20,

    /// <summary>Believed correct, never verified against the shipped corpus.</summary>
    Untested = 30,

    /// <summary>A caveat worth reading, with nothing for the tool to act on.</summary>
    Remark = 40,

    /// <summary>
    ///     The version the element first appeared in, carried in <see cref="SchemaNote.Value" />
    ///     rather than in a sentence.
    /// </summary>
    Since = 50
}
