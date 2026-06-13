// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Configuration;

public record BaselineSourceConfig
{
    public BaselineSourceType Type { get; init; } = BaselineSourceType.Http;

    public string Url { get; init; } =
        "https://raw.githubusercontent.com/AlamoEngine-Tools/eaw-baseline/refs/heads/main/foc/aet-pg-swg-lsp-foc-baseline.aet";

    public string? LocalPath { get; init; }
}