// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Server.Startup;

/// <summary>
///     How the last index scan used the per-layer snapshots, as counts.
/// </summary>
/// <param name="LayersFromSnapshot">Layers whose snapshot was valid and read.</param>
/// <param name="LayersRebuilt">Layers with no snapshot, or one discarded as stale.</param>
/// <param name="FilesReused">Documents injected from a snapshot without parsing.</param>
/// <param name="FilesParsed">Documents parsed from their text.</param>
public sealed record IndexCacheStats(int LayersFromSnapshot, int LayersRebuilt, int FilesReused, int FilesParsed);

/// <summary>How the last bone catalog build used its per-layer snapshots.</summary>
/// <param name="LayersReused">Layers whose catalog snapshot was read.</param>
/// <param name="Layers">Every layer the catalog was built for.</param>
public sealed record BoneCatalogStats(int LayersReused, int Layers);
