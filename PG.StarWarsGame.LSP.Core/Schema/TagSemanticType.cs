namespace PG.StarWarsGame.LSP.Core.Schema;

public enum TagSemanticType
{
    /// <summary>Default: no refinement. Behaviour is identical to the base ValueType alone.</summary>
    Default = 0,

    /// <summary>
    ///     Pipe-separated bitfield combination of enum values (e.g. "Infantry | Vehicle | LandHero").
    ///     Applies to DynamicEnumValue tags that are category/flag masks (DatabaseMapExport Type 14 subset).
    /// </summary>
    FlagList,

    /// <summary>
    ///     Boolean prerequisite expression over game-object references.
    ///     Applies to GameObjectTypeReferenceList tags that support AND/OR/NOT operators.
    ///     Validator implementation deferred until expression syntax is confirmed.
    /// </summary>
    PrerequisiteExpression
}