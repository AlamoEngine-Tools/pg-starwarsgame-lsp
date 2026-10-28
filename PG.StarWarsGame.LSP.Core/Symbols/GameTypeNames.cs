// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     The schema types code has to name. Every type is an id space of its own: the metafile that
///     lists a file types it (GameObjectFiles, HardPointDataFiles, FactionFiles, ...), so a hardpoint,
///     a faction and a unit may share a name without being one object.
/// </summary>
public static class GameTypeNames
{
    /// <summary>Everything GameObjectFiles lists - units, structures, projectiles, particles, planets.</summary>
    public const string GameObjectType = "GameObjectType";

    /// <summary>Everything HardPointDataFiles lists.</summary>
    public const string HardPoint = "HardPoint";

    /// <summary>Everything FactionFiles lists.</summary>
    public const string Faction = "Faction";

    /// <summary>Everything SFXEventFiles lists.</summary>
    public const string SfxEvent = "SFXEvent";

    /// <summary>Everything CommandBarComponentFiles lists.</summary>
    public const string CommandBarComponent = "CommandBarComponent";
}