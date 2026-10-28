// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace PG.StarWarsGame.LSP.Server.Variants;

/// <summary>
///     Request for the fully merged (effective) form of a variant GameObject, backing the
///     "Show effective object" virtual document in the editor client.
/// </summary>
[Method("aet/getEffectiveObject", Direction.ClientToServer)]
public sealed record GetEffectiveObjectParams : IRequest<GetEffectiveObjectResult>
{
    /// <summary>Id (Name) of the object to resolve.</summary>
    public string ObjectId { get; init; } = string.Empty;

    /// <summary>
    ///     The type whose object is meant - every type is an id space of its own. None from a document
    ///     opened before the type travelled with the request: the object is then looked up by id.
    /// </summary>
    public string? TypeName { get; init; }
}