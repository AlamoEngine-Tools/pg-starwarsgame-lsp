// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     Observation: a variant's base chain is longer than the ten sweeps the engine gives variant
///     resolution.
/// </summary>
/// <remarks>
///     <para>
///         Resolution is not recursive. The engine loops over everything repeatedly, overlaying
///         whatever has a fully-loaded base, and stops after ten passes. How many passes a chain
///         needs depends on DECLARATION ORDER: an object becomes "fully loaded" the instant its own
///         overlay succeeds, because the base's loaded flag is copied along with everything else
///         and is not restored afterwards.
///     </para>
///     <para>
///         So base-first declaration resolves any depth in a single pass, while each variant
///         appearing before its base costs one pass per link. Ten or fewer always resolves whatever
///         the order; deeper is neither reliably broken nor reliably fine, which is why this is a
///         warning about what MAY happen rather than a report of a failure - and inserting one file
///         can flip it either way, silently.
///     </para>
/// </remarks>
/// <param name="ObjectId">The variant at the deep end of the chain.</param>
/// <param name="BaseCount">How many bases the chain has above this object.</param>
/// <param name="RootBaseId">The base at the far end of the chain.</param>
public sealed record VariantChainTooDeepFact(
    string DocumentUri,
    int Line,
    int Column,
    int Length,
    string ObjectId,
    int BaseCount,
    string RootBaseId
) : XmlFact(DocumentUri, Line, Column, Length);
