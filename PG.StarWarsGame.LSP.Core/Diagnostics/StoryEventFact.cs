// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: a story Event_Type or Reward_Type tag was found.
///     <see cref="Def" /> is null when the type name is not in the schema.
/// </summary>
public sealed record StoryEventFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string EventType,
    bool IsReward,
    EnumValueDefinition? Def) : XmlFact(DocumentUri, Line, Column, Length);