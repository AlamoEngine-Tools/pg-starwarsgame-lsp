namespace PG.StarWarsGame.LSP.Core.Schema;

public enum ReferenceKind
{
    /// <summary>Non-reference type — no semantic constraint on the value.</summary>
    None,

    /// <summary>
    ///     Name attribute of an XML-defined object. See <see cref="XmlTagDefinition.ReferenceType" /> for the specific
    ///     target pool (e.g. "SFXEvent", "Faction", "GameObjectType").
    /// </summary>
    XmlObject,

    /// <summary>.alo 3D model asset filename.</summary>
    ModelFile,

    /// <summary>.tga / .dds texture or icon asset filename.</summary>
    TextureFile,

    /// <summary>Audio sample filename resolved by the engine SFX system.</summary>
    AudioFile,

    /// <summary>Animation bone name embedded in a 3D model.</summary>
    BoneName,

    /// <summary>Localisation string key (TEXT_xxx format).</summary>
    LocalisationKey,

    /// <summary>Enum value — either a dynamic XML enum (see <see cref="XmlTagDefinition.EnumName" />) or a hardcoded C++ enum.</summary>
    Enum,

    /// <summary>Reference kind cannot be classified; LSP skips validation for this tag.</summary>
    Unknown
}