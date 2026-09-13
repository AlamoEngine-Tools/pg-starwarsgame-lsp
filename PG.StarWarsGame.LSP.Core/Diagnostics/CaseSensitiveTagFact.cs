// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: a tag whose spelling the engine matches byte for byte, spelled some other way.
/// </summary>
/// <remarks>
///     <para>
///         Almost every tag is case-insensitive, because <c>DatabaseMapClass</c> uppercases the key
///         before matching it. The exceptions are tags with a hand-rolled parser, which brings its
///         own rules: <c>StoryModeClass::Load_Plots</c> compares with <c>std::operator==</c>.
///     </para>
///     <para>
///         Carries the consequence rather than deriving it, because the two plot tags fail
///         differently. A mis-cased <c>Active_Plot</c> becomes the <c>is_active = false</c> argument
///         to <c>Load_Single_Plot</c> and loads SUSPENDED - present, registered, and never started.
///         A mis-cased <c>Suspended_Plot</c> lands on that same default and so behaves correctly by
///         accident, which is worth saying but is not worth an error.
///     </para>
/// </remarks>
/// <param name="Authored">The spelling in the file, as written.</param>
/// <param name="Expected">The only spelling the engine's comparison accepts.</param>
/// <param name="Consequence">What the engine does with it instead, as a sentence.</param>
/// <param name="ChangesBehaviour">
///     Whether the mis-spelling actually changes what the game does. False where the wrong spelling
///     happens to reach the same outcome.
/// </param>
public sealed record CaseSensitiveTagFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string Authored,
    string Expected,
    string Consequence,
    bool ChangesBehaviour) : XmlFact(DocumentUri, Line, Column, Length);
