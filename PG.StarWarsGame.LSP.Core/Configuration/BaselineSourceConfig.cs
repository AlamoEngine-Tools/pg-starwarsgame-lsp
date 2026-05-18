// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Configuration;

public record BaselineSourceConfig
{
    public BaselineSourceType Type { get; init; } = BaselineSourceType.Http;

    public string EawUrl { get; init; } =
        "https://github.com/AlamoEngine-Tools/pg-eaw-baselines/releases/latest/download/eaw-baseline.json";

    public string FocUrl { get; init; } =
        "https://github.com/AlamoEngine-Tools/pg-eaw-baselines/releases/latest/download/foc-baseline.json";

    public string? LocalPath { get; init; }
}
