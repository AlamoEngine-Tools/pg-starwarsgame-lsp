// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Project;

/// <summary>
///     One project's icon sources: the directory its settings are relative to, and what it declares.
/// </summary>
/// <param name="RootPath">
///     The project's own directory. Every path in <paramref name="Settings" /> is relative to this,
///     so a dependency resolves against its own root rather than the leaf's - which is what makes a
///     referenced project's icons reachable at all.
/// </param>
/// <remarks>
///     Callers pass these highest precedence first: the leaf mod, then each dependency. The two
///     kinds of source layer differently and deliberately. A MEGA TEXTURE replaces rather than
///     merges, so the first layer that ships one wins outright and no second atlas is read - that is
///     what keeps a build pipeline which packs dependency icons into the leaf's atlas
///     authoritative. LOOSE sources are individual files, so they layer by name like every other
///     resource and the leaf's copy wins.
/// </remarks>
public sealed record IconLayer(string RootPath, IconProjectSettings Settings);
