// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     A cloaking unit whose model has no part with BASEMESH in its name, so the cloak hides nothing.
/// </summary>
/// <remarks>
///     Measured in the game: a cloak hides every part whose name contains BASEMESH. On the unit's
///     XML object, never on the model: a model shared with a unit that cannot cloak is fine there.
/// </remarks>
/// <param name="ObjectId">The cloaking unit.</param>
/// <param name="ModelName">The model the unit draws.</param>
/// <param name="Ability">The cloak's element, <c>Stealth_Ability</c> or <c>Force_Cloak_Ability</c>.</param>
/// <param name="LookChangesNothing">Whether the cloak's colour and fade change nothing either.</param>
public sealed record CloakHidesNothingFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string ObjectId,
    string ModelName,
    string Ability,
    bool LookChangesNothing
) : XmlFact(DocumentUri, Line, Column, Length);

/// <summary>
///     A cloaking unit whose model has no part with STEALTH in its name, so the cloak shows nothing.
/// </summary>
/// <remarks>Measured in the game: a cloak shows every part whose name contains STEALTH.</remarks>
/// <param name="ObjectId">The cloaking unit.</param>
/// <param name="ModelName">The model the unit draws.</param>
/// <param name="Ability">The cloak's element, <c>Stealth_Ability</c> or <c>Force_Cloak_Ability</c>.</param>
/// <param name="LookChangesNothing">Whether the cloak's colour and fade change nothing either.</param>
public sealed record CloakShowsNoShellFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string ObjectId,
    string ModelName,
    string Ability,
    bool LookChangesNothing
) : XmlFact(DocumentUri, Line, Column, Length);

/// <summary>
///     A unit with a Force cloak and a hardpoint that attaches a model, which stays drawn while the
///     unit is cloaked.
/// </summary>
/// <remarks>
///     Measured in the game: a stealth hides the models a hardpoint attaches, a Force cloak does not.
/// </remarks>
/// <param name="ObjectId">The cloaking unit.</param>
/// <param name="HardpointId">The hardpoint attaching the model.</param>
/// <param name="AttachedModel">The hardpoint's <c>Model_To_Attach</c>.</param>
public sealed record ForceCloakKeepsAttachedModelFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string ObjectId,
    string HardpointId,
    string AttachedModel
) : XmlFact(DocumentUri, Line, Column, Length);