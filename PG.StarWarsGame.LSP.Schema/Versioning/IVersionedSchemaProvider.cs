// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Schema.Versioning;

/// <summary>
///     A schema provider that checks the manifest's declared version before loading. Lets the
///     startup pipeline report an incompatible schema to the user without knowing which provider
///     was selected.
/// </summary>
public interface IVersionedSchemaProvider
{
    /// <summary>
    ///     Result of the most recent version check, or null before the first load attempt. When
    ///     <see cref="SchemaVersionCheck.CanLoad" /> is false the provider deliberately loaded
    ///     nothing.
    /// </summary>
    SchemaVersionCheck? LastVersionCheck { get; }
}
