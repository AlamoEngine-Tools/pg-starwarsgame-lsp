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
    ReferenceGroup,

    /// <summary>
    ///     Declares that the enclosing GameObject is a variant of an existing object: the tag's value
    ///     is the id of the base object (which must be the same GameObject type). The variant inherits
    ///     the base's tags, then applies its own per the each tag's <see cref="VariantMode" />.
    ///     <c>referenceKind</c> is <c>xmlObject</c>; the target type is the enclosing object's own type,
    ///     resolved from the ancestor chain rather than a fixed <c>referenceType</c>.
    /// </summary>
    VariantParent,

    /// <summary>
    ///     The reference target is scoped to the enclosing game object: the parser prefixes the target
    ///     name with the owning object's ID and a <c>$</c> separator (e.g. <c>MY_UNIT$Medic_Healing</c>).
    ///     Used by <c>GUI_Activated_Ability_Name</c> to cross-reference an ability defined in the same
    ///     game object's <c>Abilities</c> list without colliding with same-named abilities in other units.
    ///     Display layers strip the owner prefix when showing the value to the user.
    /// </summary>
    OwnerScopedReference,

    /// <summary>
    ///     The mirror image of <see cref="OwnerScopedReference" />: the tag names an owner-scoped symbol
    ///     (an ability) by its bare name, and the engine accepts any ability with that name regardless of
    ///     which object defines it. Used by the galactic ability lists in GameConstants
    ///     (<c>Activated_Sabotage_Ability_Names</c> and friends) and <c>BlackMarketItem.Ability_Names</c>.
    ///     Because abilities are indexed as <c>{ownerId}$Name</c>, a bare name resolves against nothing,
    ///     so the parser records these references with the
    ///     <see cref="PG.StarWarsGame.LSP.Core.Symbols.OwnerAgnosticReferenceId" /> marker and resolution
    ///     goes through <see cref="PG.StarWarsGame.LSP.Core.Symbols.GameIndex.ResolveOwnerAgnostic" />.
    /// </summary>
    OwnerAgnosticReference,

    /// <summary>
    ///     The list holds repeated (object, battle mode) pairs flattened into one comma-separated
    ///     value, e.g. <c>Fondor, land, Geonosis, land, Jabiim, space</c>: every even slot names an
    ///     object (<c>referenceType</c>), every odd slot the <c>StoryBattleMode</c> it applies in.
    ///     Used by <c>Campaign.Autoresolve_Exclusion_Locations</c>. Refines the canonical
    ///     <see cref="XmlValueType.TypeReferenceList" /> rather than being a value type of its own -
    ///     the engine has no such type, only a list whose tokens alternate in meaning.
    /// </summary>
    PlanetModePairList
}