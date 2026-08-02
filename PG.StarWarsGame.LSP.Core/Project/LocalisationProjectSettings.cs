// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Project;

// One localisation source per project: the directory (relative to the .pgproj) holding the
// translation files, and the format they're stored in. Replaces the old
// directories.text/directories.textResourceType pair, which bolted a format annotation onto a
// list of plain directory roles.
// Credits is optional and null means "use the naming convention", so an existing .pgproj that
// declares nothing keeps working unchanged.
public sealed record LocalisationProjectSettings(
    string Type, string Directory, LocalisationCreditsSettings? Credits = null);