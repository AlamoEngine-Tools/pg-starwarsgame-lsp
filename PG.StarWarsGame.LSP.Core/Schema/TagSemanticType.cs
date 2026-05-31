// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

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
    PrerequisiteExpression,

    /// <summary>
    ///     Groups the parent XML objects that share this tag's value under a common key.
    ///     <c>referenceKind</c> and <c>referenceType</c> in YAML identify the type of objects being grouped.
    ///     No unresolved-reference diagnostic is emitted; instead a
    ///     <see cref="PG.StarWarsGame.LSP.Core.Symbols.GroupMembership" />
    ///     entry is produced linking the group key to the parent object's definition position.
    /// </summary>
    ReferenceGroup
}