// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>
///     Kind names the server asks for by name rather than through a schema reference.
/// </summary>
/// <remarks>
///     Almost nothing belongs here. A slot's kind comes from its own <c>referenceType</c>, so the
///     code never needs to know which kinds exist - the exceptions are the few places that ask a
///     question of their own, such as the story simulator wanting to know whether a name the author
///     typed is a planet. A name that no longer exists in the schema simply resolves to null there,
///     and the caller falls back to its untyped behaviour.
/// </remarks>
public static class ObjectKindNames
{
    public const string Planet = "Planet";
}
