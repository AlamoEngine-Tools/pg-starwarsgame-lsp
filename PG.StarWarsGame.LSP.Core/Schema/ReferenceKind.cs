// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

public enum ReferenceKind
{
    /// <summary>Non-reference type - no semantic constraint on the value.</summary>
    None,

    /// <summary>
    ///     Name attribute of an XML-defined object. See <see cref="XmlTagDefinition.ObjectType" /> for the resolved
    ///     target pool (e.g. Faction, SFXEvent, GameObjectType).
    /// </summary>
    XmlObject,

    /// <summary>.alo 3D model asset filename.</summary>
    ModelFile,

    /// <summary>.tga / .dds texture or icon asset filename.</summary>
    TextureFile,

    /// <summary>Audio sample filename resolved by the engine SFX system.</summary>
    AudioFile,

    /// <summary>Alamo Engine tactical map file (.ted).</summary>
    MapFile,

    /// <summary>Animation bone name embedded in a 3D model.</summary>
    BoneName,

    /// <summary>Localisation string key (TEXT_xxx format).</summary>
    LocalisationKey,

    /// <summary>Enum value - either a dynamic XML enum (see <see cref="XmlTagDefinition.Enum" />) or a hardcoded C++ enum.</summary>
    Enum,

    /// <summary>
    ///     Each space/comma-separated token must exist in a <see cref="HardcodedReferenceSet" />.
    ///     See <see cref="XmlTagDefinition.HardcodedSet" /> for the resolved set.
    /// </summary>
    HardcodedSet,

    /// <summary>Reference kind cannot be classified; LSP skips validation for this tag.</summary>
    Unknown
}