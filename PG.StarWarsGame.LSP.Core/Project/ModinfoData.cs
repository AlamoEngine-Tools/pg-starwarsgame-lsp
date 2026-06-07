// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Project;

public sealed record ModinfoData(
    string Name,
    string? Version,
    string? Summary,
    string? Icon,
    IReadOnlyList<ModinfoLanguageInfo> Languages,
    object? Custom);
