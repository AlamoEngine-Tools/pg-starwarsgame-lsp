// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Configuration;

public record SchemaSourceConfig
{
    public SchemaSourceType Type { get; init; } = SchemaSourceType.Http;

    /// <summary>
    ///     Base URL of a schema's <c>eaw/</c> folder. Empty (the default) means the newest release of
    ///     <see cref="Repository" /> this server supports.
    /// </summary>
    public string Url { get; init; } = "";

    /// <summary><c>owner/name</c> of the repository whose releases are resolved when <see cref="Url" /> is empty.</summary>
    public string Repository { get; init; } = "AlamoEngine-Tools/eaw-schema";

    public string? LocalPath { get; init; }
}