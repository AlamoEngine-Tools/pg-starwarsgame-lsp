// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     A hardpoint's <c>Special_Ability_Name</c> names an ability that the object mounting the
///     hardpoint does not have. The engine enables the ability on the mounting object while the
///     hardpoint is alive, so an ability that object never defines simply never activates.
///     <para>
///         Checked against the mounting object's whole <c>Variant_Of_Existing_Type</c> chain, because a
///         variant inherits its base's abilities while the ability symbols stay indexed under the base
///         that declares them.
///     </para>
/// </summary>
/// <param name="HardpointId">The hardpoint naming the ability.</param>
/// <param name="AbilityName">The ability that is missing.</param>
/// <param name="OwnerId">The object mounting the hardpoint.</param>
/// <param name="DefinedElsewhere">
///     True when some other object does define an ability of that name - the difference between a
///     typo and an ability attached to the wrong unit, which are fixed differently.
/// </param>
public sealed record HardpointAbilityNotOnOwnerFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string HardpointId,
    string AbilityName,
    string OwnerId,
    bool DefinedElsewhere
) : XmlFact(DocumentUri, Line, Column, Length);
