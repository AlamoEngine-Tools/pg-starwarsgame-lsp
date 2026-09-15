// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: a system spy's <c>Duration_In_Secs</c> on the wrong side of zero for the
///     activation style it declares.
/// </summary>
/// <remarks>
///     <para>
///         <c>SystemSpyAbilityClass::Validate_Data</c> (<c>0101dcdf</c>) branches on the style and
///         demands the OPPOSITE sign in each arm: under <c>Galactic_Automatic</c> it complains when
///         <c>0.0 &lt;= duration</c> and writes <c>-1.0</c>; under <c>Ground_Activated</c> it
///         complains when <c>duration &lt;= 0.0</c> and writes <c>30.0</c>. Both comparisons are
///         strict about zero, and both parentheticals in the engine's own message - "(Should be
///         &lt; 0)" and "(Should be &gt; 0)" - agree with the code for once.
///     </para>
///     <para>
///         A range on the tag could not express this. The same value is correct under one style and
///         refused under the other, which is also why the first harvest missed it: that pass keyed
///         on the tag column, and here the message opens with the object name instead.
///     </para>
///     <para>
///         The third arm of the branch - any other style - is the schema's business rather than
///         this fact's. <c>Activation_Style</c> on <c>System_Spy_Ability</c> carries
///         <c>allowedValues</c> for the two styles the class supports, so an unsupported one is
///         already reported where it is written, and guessing a bound for it here would invent one.
///     </para>
/// </remarks>
/// <param name="Style">The style as authored, so the message quotes the file rather than an enum.</param>
/// <param name="Duration">The duration as authored, for the message.</param>
/// <param name="Requirement">
///     Which side of zero this style needs, as a sentence fragment completing "needs a duration
///     ...".
/// </param>
/// <param name="Repair">What the engine writes over it - <c>-1.0</c> or <c>30.0</c>.</param>
public sealed record SystemSpyDurationFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string Style,
    double Duration,
    string Requirement,
    string Repair) : XmlFact(DocumentUri, Line, Column, Length);