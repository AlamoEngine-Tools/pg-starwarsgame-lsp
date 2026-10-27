// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: one token of a tag whose values the engine hardcodes (a <c>HardcodedSet</c>
///     reference) is not a value the game accepts there. The span is the token's own.
/// </summary>
/// <param name="Token">The token as written.</param>
/// <param name="SetName">The hardcoded set the tag draws from.</param>
/// <param name="Deprecated">
///     True when the set knows the token but marks it deprecated; false when the set does not know
///     it at all, or the tag's value groups rule it out.
/// </param>
public sealed record HardcodedSetValueFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string Token,
    string SetName,
    bool Deprecated) : XmlFact(DocumentUri, Line, Column, Length);