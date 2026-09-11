// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: two tags that must hold a relation to each other do not.
/// </summary>
/// <remarks>
///     Neither value is wrong alone - a radius of 500 is fine, and so is a chase radius of 100 -
///     which is why this needs both tags at once and cannot be a value check.
/// </remarks>
/// <param name="LeftTag">The tag the rule is anchored on.</param>
/// <param name="RightTag">The tag it is compared against.</param>
/// <param name="Left">The left value as authored.</param>
/// <param name="Right">The right value as authored.</param>
/// <param name="Expectation">
///     The relation as a sentence fragment, phrased as the engine phrases it, completing
///     "&lt;LeftTag&gt; ... &lt;RightTag&gt;".
/// </param>
public sealed record TagComparisonFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string LeftTag,
    string RightTag,
    double Left,
    double Right,
    string Expectation) : XmlFact(DocumentUri, Line, Column, Length);
