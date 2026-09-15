// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: a hardpoint is declared destroyable but nothing can ever hit it.
/// </summary>
/// <remarks>
///     <para>
///         Two shapes, one consequence. A destroyable hardpoint with no <c>Collision_Mesh</c> has
///         nothing for damage to be routed to; two destroyable hardpoints on one object naming the
///         same mesh compete for a lookup that returns the FIRST match, so the later one is dead
///         weight.
///     </para>
///     <para>
///         Both rest on the damage-routing pass over the 2018 binary - damage reaches a hardpoint
///         through its collision mesh, first match wins - rather than on any engine message. The
///         engine says nothing about either, which is the reason to say something: the author gets
///         a hardpoint that shows in the UI, takes a share of the object's health, and can never be
///         destroyed.
///     </para>
/// </remarks>
/// <param name="HardpointId">The hardpoint that cannot be hit.</param>
/// <param name="SharedWith">
///     The hardpoint that already claims this collision mesh and wins the lookup, or null when the
///     problem is simply that no mesh was named.
/// </param>
public sealed record HardpointUnhittableFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string HardpointId,
    string? SharedWith) : XmlFact(DocumentUri, Line, Column, Length);