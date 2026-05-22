// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Configuration;

public record SchemaSourceConfig
{
    public SchemaSourceType Type { get; init; } = SchemaSourceType.Http;

    public string Url { get; init; } =
        "https://raw.githubusercontent.com/AlamoEngine-Tools/eaw-schema/refs/heads/main/eaw/";

    public string? LocalPath { get; init; }
}