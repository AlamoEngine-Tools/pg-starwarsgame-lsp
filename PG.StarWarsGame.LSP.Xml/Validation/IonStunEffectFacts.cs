// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Xml.Validation;

/// <summary>
///     A type without the ION_STUN_EFFECT behaviour whose model draws ion stun effects, which are
///     then drawn from the start and never hidden.
/// </summary>
/// <remarks>
///     Measured in the game: the ION_STUN_EFFECT behaviour hides every effect whose name starts with
///     <c>pi</c> when the object is created, shows them for a stun and hides them at its end. Nothing
///     else writes that group, so without the behaviour the effects stay as the model file has them.
///     On the type's XML object, never on the model: a model shared with a type that has the
///     behaviour is fine there.
/// </remarks>
/// <param name="ObjectId">The type without the behaviour.</param>
/// <param name="ModelName">The model the type draws.</param>
/// <param name="Effects">The effects the file marks visible, each name once, in file order.</param>
public sealed record IonEffectsNeverHiddenFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string ObjectId,
    string ModelName,
    IReadOnlyList<string> Effects
) : XmlFact(DocumentUri, Line, Column, Length);