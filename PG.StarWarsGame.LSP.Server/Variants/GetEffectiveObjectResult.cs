// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Variants;

/// <summary>Result of <c>aet/getEffectiveObject</c>.</summary>
/// <param name="Found">Whether the object id resolved.</param>
/// <param name="Cyclic">Whether the variant base chain contains a cycle.</param>
/// <param name="CycleObjectId">Id at which the chain looped, when cyclic.</param>
/// <param name="Chain">Base chain from most-derived to root.</param>
/// <param name="Xml">The rendered effective XML with provenance comments.</param>
/// <param name="TypeName">The resolved object's GameObject type.</param>
public sealed record GetEffectiveObjectResult(
    bool Found,
    bool Cyclic,
    string? CycleObjectId,
    IReadOnlyList<string> Chain,
    string Xml,
    string? TypeName
);