namespace PG.StarWarsGame.LSP.Core.Schema;

public enum EnumKind
{
    /// <summary>Hardcoded C++ enum. Values in the schema YAML are authoritative.</summary>
    SchemaFixed,

    /// <summary>Defined in Xml/Enum/*.xml; base values are populated at runtime by the Assets layer.</summary>
    DynamicXml,

    /// <summary>Enum defined in gameconstants.xml</summary>
    GameConstants
}