// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Project;

public sealed record ModProjectFile(
    string Name,
    ModinfoData? Modinfo,
    DirectoryMap Directories,
    IReadOnlyList<ProjectReference> ProjectReferences,
    LocalisationProjectSettings? Localisation = null);