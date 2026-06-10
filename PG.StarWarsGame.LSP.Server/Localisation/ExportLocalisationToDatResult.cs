// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Localisation;

public sealed record ExportLocalisationToDatResult(IReadOnlyList<string> WrittenFiles, string? Error)
{
    public static readonly ExportLocalisationToDatResult Empty = new([], null);
}
