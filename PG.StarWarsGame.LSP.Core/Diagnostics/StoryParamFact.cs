// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: a story Event_Param or Reward_Param slot was inspected.
///     <see cref="Def" /> is null when no schema definition exists for this slot (excess slot).
///     <see cref="RawValue" /> is empty when the slot is absent or empty (used for required-param checks).
/// </summary>
public sealed record StoryParamFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string EventType,
    bool IsReward,
    int SlotPosition,
    ParamDefinition? Def,
    string RawValue) : XmlFact(DocumentUri, Line, Column, Length);