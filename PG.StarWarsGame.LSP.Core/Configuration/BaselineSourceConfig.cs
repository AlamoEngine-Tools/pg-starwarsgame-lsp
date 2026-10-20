// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Configuration;

public record BaselineSourceConfig
{
    public BaselineSourceType Type { get; init; } = BaselineSourceType.Http;

    /// <summary>
    ///     URL of the baseline <c>.aet</c> file. Empty (the default) means the newest release of
    ///     <see cref="Repository" /> whose baseline this server reads; the icon sidecar is read from
    ///     beside it either way.
    /// </summary>
    public string Url { get; init; } = "";

    /// <summary><c>owner/name</c> of the repository whose releases are resolved when <see cref="Url" /> is empty.</summary>
    public string Repository { get; init; } = "AlamoEngine-Tools/eaw-baseline";

    /// <summary>The baseline file inside that repository, at the resolved tag.</summary>
    public string FilePath { get; init; } = "foc/aet-pg-swg-lsp-foc-baseline.aet";

    public string? LocalPath { get; init; }
}