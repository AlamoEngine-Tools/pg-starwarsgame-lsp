// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     An object's <c>Land_Damage_Alternates</c> names a damage stage that nothing in its model is
///     tagged for, so the unit reaches that state and does not change.
/// </summary>
/// <remarks>
///     <para>
///         ONE direction. A model tagging more stages than the XML uses is never reported: that is an
///         asset carrying more than this object asks of it, the same shape as a <c>PTE_</c> effect on
///         a unit with no TURBO, or a stealth shell on a unit that cannot cloak.
///     </para>
///     <para>
///         Reported only where the model's names could actually be read - an uncatalogued model makes
///         the question undecidable, and a warning then would be about the index rather than about
///         the file.
///     </para>
/// </remarks>
/// <param name="ObjectId">The object declaring the stages.</param>
/// <param name="ModelName">The model that has nothing tagged for them.</param>
/// <param name="MissingStages">The declared stages the model tags nothing for, ascending.</param>
/// <param name="TaggedStages">The stages the model DOES tag, ascending, so the reader sees the gap.</param>
public sealed record DamageStageNotOnModelFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string ObjectId,
    string ModelName,
    IReadOnlyList<int> MissingStages,
    IReadOnlyList<int> TaggedStages
) : XmlFact(DocumentUri, Line, Column, Length);
