// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     Loads the EaW/FoC XML schema and the Lua API schema from the source configured in
///     <c>initializationOptions</c>, then makes them queryable. Awaited as the first pipeline stage:
///     the schema must be fully present before any indexing begins. Treated as static for the
///     editor session — no hot-reload.
/// </summary>
public interface ISchemaBootstrapper
{
    Task LoadAsync(CancellationToken ct);
}