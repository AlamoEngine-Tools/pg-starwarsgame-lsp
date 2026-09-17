// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Diagnostics;

/// <summary>
///     The single registry of diagnostic ids. Every diagnostic the server publishes names one of
///     these; nothing constructs a <see cref="DiagnosticId" /> inline.
///     <para>
///         Ids are a published contract - users write them into suppression comments and commit
///         them. Append within a group and never renumber or reuse: a retired diagnostic's number
///         stays retired, because someone's file still names it. <c>DiagnosticIdCatalogueTest</c>
///         enforces uniqueness and format; it cannot enforce stability, so that part is on us.
///     </para>
/// </summary>
public static class DiagnosticIds
{
    // ── References ──
    public static readonly DiagnosticId AbilityDefinitionSubObjectList = new(DiagnosticGroup.References, 1);
    public static readonly DiagnosticId AbilitySfxMap = new(DiagnosticGroup.References, 2);
    public static readonly DiagnosticId ConditionalSfxEvent = new(DiagnosticGroup.References, 3);
    public static readonly DiagnosticId ConditionalSpeechEvent = new(DiagnosticGroup.References, 4);
    public static readonly DiagnosticId ContextNameList = new(DiagnosticGroup.References, 5);
    public static readonly DiagnosticId ContextNamePair = new(DiagnosticGroup.References, 6);
    public static readonly DiagnosticId FactionReference = new(DiagnosticGroup.References, 7);
    public static readonly DiagnosticId GuiActivatedAbilityDefinitionSubObjectList = new(DiagnosticGroup.References, 8);
    public static readonly DiagnosticId HardPointSfxMap = new(DiagnosticGroup.References, 9);
    public static readonly DiagnosticId MusicEventReference = new(DiagnosticGroup.References, 10);
    public static readonly DiagnosticId NameReference = new(DiagnosticGroup.References, 11);
    public static readonly DiagnosticId NameReferenceList = new(DiagnosticGroup.References, 12);
    public static readonly DiagnosticId PerFactionObjectList = new(DiagnosticGroup.References, 13);
    public static readonly DiagnosticId PerFactionPlanet = new(DiagnosticGroup.References, 14);
    public static readonly DiagnosticId PresenceInducedAnimations = new(DiagnosticGroup.References, 15);
    public static readonly DiagnosticId SfxEventHudReference = new(DiagnosticGroup.References, 16);
    public static readonly DiagnosticId SFXEventReference = new(DiagnosticGroup.References, 17);
    public static readonly DiagnosticId SpeechEventReference = new(DiagnosticGroup.References, 18);
    public static readonly DiagnosticId TypeReference = new(DiagnosticGroup.References, 19);
    public static readonly DiagnosticId TypeReferenceList = new(DiagnosticGroup.References, 20);
    public static readonly DiagnosticId UnresolvedReference = new(DiagnosticGroup.References, 21);
    public static readonly DiagnosticId StoryDialogReference = new(DiagnosticGroup.References, 22);
    public static readonly DiagnosticId ContextNamePairUnresolvedMusicEvent = new(DiagnosticGroup.References, 23);
    public static readonly DiagnosticId NameReferenceEmpty = new(DiagnosticGroup.References, 24);
    public static readonly DiagnosticId PerFactionObjectListUnknownFaction = new(DiagnosticGroup.References, 25);
    public static readonly DiagnosticId PerFactionObjectListEmpty = new(DiagnosticGroup.References, 26);
    public static readonly DiagnosticId StoryDialogChapterNotDefined = new(DiagnosticGroup.References, 27);

    /// <summary>
    ///     A reference whose expected type has NO indexed instances anywhere in the workspace - so the
    ///     tool cannot tell whether the name is wrong, and says so instead of guessing.
    /// </summary>
    /// <remarks>
    ///     Deliberately NOT <see cref="UnresolvedReference" />, and deliberately Information rather
    ///     than Error. Two scopes reach this today and neither is the author's mistake: the AI tree is
    ///     skipped by <c>EaWXmlContext</c> until a parser for its format exists, and GRAPHICDETAILS.XML
    ///     emits no type symbols yet. Reporting those as "No object with this name exists in the
    ///     workspace" states something false about the author's data.
    ///     It carries its own id because the two are suppressed separately: silencing "we cannot check
    ///     this yet" must never silence a genuine missing reference. It is Information rather than a
    ///     Hint because the author should see the limitation, and rather than a Warning because
    ///     nothing is wrong with what they wrote.
    ///     The rule is a zero-instance count, not a list of known-unsupported scopes: it needs no
    ///     maintenance and it stops firing by itself the moment a type starts indexing.
    /// </remarks>
    public static readonly DiagnosticId ReferenceTypeNotIndexed = new(DiagnosticGroup.References, 30);

    /// <summary>A <c>require()</c> naming a module with no matching <c>.lua</c> file.</summary>
    public static readonly DiagnosticId LuaUnresolvedModule = new(DiagnosticGroup.References, 28);

    /// <summary>A dialog command argument naming something that does not resolve.</summary>
    public static readonly DiagnosticId DialogArgReference = new(DiagnosticGroup.References, 29);

    // ── Enums ──
    public static readonly DiagnosticId AbilityModFlag = new(DiagnosticGroup.Enums, 1);
    public static readonly DiagnosticId Audio3dProviderName = new(DiagnosticGroup.Enums, 2);
    public static readonly DiagnosticId CableRenderMode = new(DiagnosticGroup.Enums, 3);
    public static readonly DiagnosticId CommandBarProperty = new(DiagnosticGroup.Enums, 4);
    public static readonly DiagnosticId ProjectileCategory = new(DiagnosticGroup.Enums, 5);
    public static readonly DiagnosticId ShipClassType = new(DiagnosticGroup.Enums, 6);
    public static readonly DiagnosticId StoryParamEnum = new(DiagnosticGroup.Enums, 7);
    public static readonly DiagnosticId VictoryConditionList = new(DiagnosticGroup.Enums, 8);
    public static readonly DiagnosticId DynamicEnumValue = new(DiagnosticGroup.Enums, 9);
    public static readonly DiagnosticId AbilitySfxMapUnknownAbilityType = new(DiagnosticGroup.Enums, 10);
    public static readonly DiagnosticId DamageToArmorModUnknownArmorType = new(DiagnosticGroup.Enums, 11);
    public static readonly DiagnosticId DamageToArmorModUnknownDamageType = new(DiagnosticGroup.Enums, 12);
    public static readonly DiagnosticId HardPointSfxMapUnknownType = new(DiagnosticGroup.Enums, 13);
    public static readonly DiagnosticId InaccuracyMapUnknownCategory = new(DiagnosticGroup.Enums, 14);
    public static readonly DiagnosticId PlanetModeUnknownMode = new(DiagnosticGroup.Enums, 15);

    /// <summary>A dialog command the schema's command set does not contain.</summary>
    public static readonly DiagnosticId DialogUnknownCommand = new(DiagnosticGroup.Enums, 16);

    /// <summary>
    ///     A value that the enum allows but this OWNER does not - the engine names the one style it
    ///     supports and overwrites anything else.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="DynamicEnumValue" />, which reports a value no owner accepts.
    ///     Suppressing "this ability is fussier than the enum" must not silence "that value does not
    ///     exist".
    /// </remarks>
    public static readonly DiagnosticId EnumValueNotAllowedHere = new(DiagnosticGroup.Enums, 17);

    /// <summary>
    ///     An unknown projectile category in <c>Projectile_Types_Targeted</c>, which makes a point
    ///     defence silently ignore that projectile.
    /// </summary>
    public static readonly DiagnosticId ProjectileCategoryList = new(DiagnosticGroup.Enums, 18);

    // ── Values ──
    public static readonly DiagnosticId AbilityModMultiplier = new(DiagnosticGroup.Values, 1);
    public static readonly DiagnosticId AudioParamInt = new(DiagnosticGroup.Values, 2);
    public static readonly DiagnosticId BooleanValue = new(DiagnosticGroup.Values, 3);
    public static readonly DiagnosticId DamageToArmorMod = new(DiagnosticGroup.Values, 4);
    public static readonly DiagnosticId DeathCloneSpec = new(DiagnosticGroup.Values, 5);
    public static readonly DiagnosticId FloatValue = new(DiagnosticGroup.Values, 6);
    public static readonly DiagnosticId ForceDeploymentList = new(DiagnosticGroup.Values, 7);
    public static readonly DiagnosticId InaccuracyMap = new(DiagnosticGroup.Values, 8);
    public static readonly DiagnosticId IntFloatTupleList = new(DiagnosticGroup.Values, 9);
    public static readonly DiagnosticId IntValue = new(DiagnosticGroup.Values, 10);
    public static readonly DiagnosticId MovieFrameTrigger = new(DiagnosticGroup.Values, 11);
    public static readonly DiagnosticId NormalizedFloat = new(DiagnosticGroup.Values, 12);
    public static readonly DiagnosticId PerFactionIntMap = new(DiagnosticGroup.Values, 13);
    public static readonly DiagnosticId PerFactionValue = new(DiagnosticGroup.Values, 14);
    public static readonly DiagnosticId PositionLabel = new(DiagnosticGroup.Values, 15);
    public static readonly DiagnosticId SfxCount = new(DiagnosticGroup.Values, 16);
    public static readonly DiagnosticId SfxPercentage = new(DiagnosticGroup.Values, 17);
    public static readonly DiagnosticId ShaderVersionHex = new(DiagnosticGroup.Values, 18);
    public static readonly DiagnosticId TupleList = new(DiagnosticGroup.Values, 19);
    public static readonly DiagnosticId Type35 = new(DiagnosticGroup.Values, 20);
    public static readonly DiagnosticId Type36 = new(DiagnosticGroup.Values, 21);
    public static readonly DiagnosticId Type37 = new(DiagnosticGroup.Values, 22);
    public static readonly DiagnosticId Type38 = new(DiagnosticGroup.Values, 23);
    public static readonly DiagnosticId Uint = new(DiagnosticGroup.Values, 24);
    public static readonly DiagnosticId UnitSpawnProbabilityTable = new(DiagnosticGroup.Values, 25);
    public static readonly DiagnosticId UnitSpawnTable = new(DiagnosticGroup.Values, 26);
    public static readonly DiagnosticId UvSlotIndex = new(DiagnosticGroup.Values, 27);
    public static readonly DiagnosticId VendorIdHex = new(DiagnosticGroup.Values, 28);
    public static readonly DiagnosticId FloatList = new(DiagnosticGroup.Values, 29);
    public static readonly DiagnosticId FloatTupleList = new(DiagnosticGroup.Values, 30);
    public static readonly DiagnosticId FloatVector2 = new(DiagnosticGroup.Values, 31);
    public static readonly DiagnosticId FloatVector3 = new(DiagnosticGroup.Values, 32);
    public static readonly DiagnosticId FloatVector3List = new(DiagnosticGroup.Values, 33);
    public static readonly DiagnosticId FloatVector4 = new(DiagnosticGroup.Values, 34);
    public static readonly DiagnosticId IntList = new(DiagnosticGroup.Values, 35);
    public static readonly DiagnosticId PrerequisiteExpression = new(DiagnosticGroup.Values, 36);
    public static readonly DiagnosticId RgbaValue = new(DiagnosticGroup.Values, 37);
    public static readonly DiagnosticId DynamicEnumInvalidIdentifier = new(DiagnosticGroup.Values, 38);
    public static readonly DiagnosticId DynamicEnumOrNotAllowed = new(DiagnosticGroup.Values, 39);
    public static readonly DiagnosticId DynamicEnumEmptyValue = new(DiagnosticGroup.Values, 40);
    public static readonly DiagnosticId FloatListEmpty = new(DiagnosticGroup.Values, 41);
    public static readonly DiagnosticId FloatTupleListEmpty = new(DiagnosticGroup.Values, 42);
    public static readonly DiagnosticId FloatVector3ListEmpty = new(DiagnosticGroup.Values, 43);
    public static readonly DiagnosticId IntFloatTupleListFloatWhereIntExpected = new(DiagnosticGroup.Values, 44);
    public static readonly DiagnosticId IntListFloatWhereIntExpected = new(DiagnosticGroup.Values, 45);
    public static readonly DiagnosticId IntListEmpty = new(DiagnosticGroup.Values, 46);
    public static readonly DiagnosticId IntValueOutOfRange = new(DiagnosticGroup.Values, 47);
    public static readonly DiagnosticId IntValueFloatWhereIntExpected = new(DiagnosticGroup.Values, 48);
    public static readonly DiagnosticId MovieFrameTriggerFloatWhereIntExpected = new(DiagnosticGroup.Values, 49);
    public static readonly DiagnosticId NormalizedFloatOutOfRange = new(DiagnosticGroup.Values, 50);
    public static readonly DiagnosticId PerFactionIntMapFloatWhereIntExpected = new(DiagnosticGroup.Values, 51);
    public static readonly DiagnosticId PrerequisiteExpressionEmpty = new(DiagnosticGroup.Values, 52);
    public static readonly DiagnosticId RgbaValueFloatWhereIntExpected = new(DiagnosticGroup.Values, 53);
    public static readonly DiagnosticId UintNegativeOrFractional = new(DiagnosticGroup.Values, 54);
    public static readonly DiagnosticId UnitSpawnProbabilityInvalid = new(DiagnosticGroup.Values, 55);
    public static readonly DiagnosticId UnitSpawnTableFloatWhereIntExpected = new(DiagnosticGroup.Values, 56);

    // Dialog command arguments: how many there are, and whether each is a value the command accepts.
    public static readonly DiagnosticId DialogCommandArity = new(DiagnosticGroup.Values, 57);
    public static readonly DiagnosticId DialogArgValue = new(DiagnosticGroup.Values, 58);

    // Range rules the engine states about dozens of tags in its own error messages, opted into per
    // tag by validationId rather than carried by a value type.
    public static readonly DiagnosticId ValueMustBeNonNegative = new(DiagnosticGroup.Values, 59);
    public static readonly DiagnosticId ValueMustBePositive = new(DiagnosticGroup.Values, 60);
    public static readonly DiagnosticId BonusPercentageTooLow = new(DiagnosticGroup.Values, 61);

    // Each range rule gets its own id rather than sharing one: a suppression targets an id, and a
    // shared one would mean silencing an angle check also silences a fraction check.
    public static readonly DiagnosticId AngleOutsideHalfTurn = new(DiagnosticGroup.Values, 62);
    public static readonly DiagnosticId AngleOutsideFullTurn = new(DiagnosticGroup.Values, 63);
    public static readonly DiagnosticId NegativeFractionOutOfRange = new(DiagnosticGroup.Values, 64);
    public static readonly DiagnosticId FractionNotBelowOne = new(DiagnosticGroup.Values, 65);
    public static readonly DiagnosticId ValueNotBelowOne = new(DiagnosticGroup.Values, 66);
    public static readonly DiagnosticId CurveNeedsTwoControlPoints = new(DiagnosticGroup.Values, 67);

    /// <summary>
    ///     A value below -1.0 where the engine floors at -1.0 inclusive. Its own id rather than
    ///     sharing <see cref="BonusPercentageTooLow" />: that rule excludes -1.0 and this one
    ///     allows it, so a suppression aimed at one must not silence the other.
    /// </summary>
    public static readonly DiagnosticId ValueBelowNegativeOne = new(DiagnosticGroup.Values, 68);

    /// <summary>
    ///     A duration below the one second the engine insists on, which it then substitutes. Its own
    ///     id rather than a shared minimum: the bound is a unit of time rather than a fraction, and
    ///     it applies to one owner of a tag name three types share.
    /// </summary>
    public static readonly DiagnosticId DurationBelowOneSecond = new(DiagnosticGroup.Values, 69);

    /// <summary>
    ///     The owner's share of a split income outside <c>[0, 1)</c>. Its own id rather than a range
    ///     rule's, because the engine only checks it while both split flags are on - a value the
    ///     engine never reads is not a defect.
    /// </summary>
    public static readonly DiagnosticId OwnerIncomeShareOutOfRange = new(DiagnosticGroup.Values, 70);

    /// <summary>
    ///     A per-tech-level respawn table the engine refuses - wrong length, a negative entry, or a
    ///     zero among non-zeros. One id for all three, because the engine discards the same thing
    ///     whichever check fails.
    /// </summary>
    public static readonly DiagnosticId RespawnTimeList = new(DiagnosticGroup.Values, 71);

    /// <summary>
    ///     Text longer than the fixed buffer the engine copies it into. Not a validity rule but a
    ///     crash: the copy is an unchecked <c>strcpy</c>, and the game does not start.
    /// </summary>
    /// <remarks>
    ///     Its own id, and one nobody should suppress lightly - unlike every other rule here, the
    ///     consequence is not a silent behaviour change but a game that will not load.
    /// </remarks>
    public static readonly DiagnosticId EngineTextLimit = new(DiagnosticGroup.Values, 72);

    /// <summary>
    ///     Text approaching that buffer but still inside it. Warned about because these limits are
    ///     reached by accumulation, so the useful moment to say something is while there is still
    ///     room to act.
    /// </summary>
    /// <remarks>
    ///     Its own id so it can be silenced on its own. Someone who knows their file is large and
    ///     deliberate should be able to stop the nagging without also silencing
    ///     <see cref="EngineTextLimit" />, which reports a game that will not load.
    /// </remarks>
    public static readonly DiagnosticId EngineTextLimitApproaching = new(DiagnosticGroup.Values, 73);

    /// <summary>
    ///     A list whose first entry the engine requires to be a particular name, because index 0 is
    ///     the fallback it hands out when a lookup misses - <c>Damage_Types</c> and
    ///     <c>Armor_Types</c>.
    /// </summary>
    /// <remarks>
    ///     One id for the family rather than one per list: it is a single concern from the author's
    ///     side - the default entry has to stay at the front - and someone silencing it for damage
    ///     types would mean it for armour types too.
    /// </remarks>
    public static readonly DiagnosticId RequiredFirstListEntry = new(DiagnosticGroup.Values, 74);

    /// <summary>
    ///     A weapon hardpoint's fire cone outside the span the engine can act on. Its own id rather
    ///     than sharing <see cref="AngleOutsideFullTurn" />: that rule admits zero and this one
    ///     cannot, because the engine asserts the cone is strictly greater than it.
    /// </summary>
    public static readonly DiagnosticId FireConeOutsideFullTurn = new(DiagnosticGroup.Values, 75);

    /// <summary>
    ///     A tag spelled in a casing its own parser will not accept. Rare by design: nearly every
    ///     tag is case-insensitive, and the exceptions are the ones with a hand-rolled parser.
    /// </summary>
    public static readonly DiagnosticId CaseSensitiveTag = new(DiagnosticGroup.Structure, 13);

    // ── Assets ──
    public static readonly DiagnosticId AudioFileExistence = new(DiagnosticGroup.Assets, 1);
    public static readonly DiagnosticId AudioFileFormat = new(DiagnosticGroup.Assets, 2);
    public static readonly DiagnosticId HardPointTypeToTextureMap = new(DiagnosticGroup.Assets, 3);
    public static readonly DiagnosticId LocalisationToTextureMap = new(DiagnosticGroup.Assets, 4);
    public static readonly DiagnosticId MapFileExistence = new(DiagnosticGroup.Assets, 5);
    public static readonly DiagnosticId MapFileFormat = new(DiagnosticGroup.Assets, 6);
    public static readonly DiagnosticId ModelFileExistence = new(DiagnosticGroup.Assets, 7);
    public static readonly DiagnosticId ModelFileFormat = new(DiagnosticGroup.Assets, 8);
    public static readonly DiagnosticId TextureFileExistence = new(DiagnosticGroup.Assets, 9);
    public static readonly DiagnosticId TextureFileFormat = new(DiagnosticGroup.Assets, 10);

    /// <summary>
    ///     An icon exists as a raw source image but is missing from the workspace's mega texture:
    ///     drawn, but never repacked. Deliberately distinct from
    ///     <see cref="TextureFileExistence" /> - the art is there and the fix is a rebuild, which is
    ///     a different instruction from "this icon does not exist".
    /// </summary>
    public static readonly DiagnosticId IconAwaitingRepack = new(DiagnosticGroup.Assets, 11);

    /// <summary>
    ///     A model resolves, but a texture it names inside itself does not.
    ///     <para>
    ///         Separate from <see cref="TextureFileExistence" />, which is about a texture the XML
    ///         itself names: this one is reported against the tag that pulls the MODEL in, because
    ///         that is the only place in the document the problem can be anchored to. The fix is
    ///         also different - the reference lives in the .alo, so it is the art that has to
    ///         change, not the line the warning appears on.
    ///     </para>
    /// </summary>
    public static readonly DiagnosticId ModelTextureExistence = new(DiagnosticGroup.Assets, 12);

    // ── Localisation ──
    public static readonly DiagnosticId LocalisationKeyExistence = new(DiagnosticGroup.Localisation, 1);
    public static readonly DiagnosticId LocalisationKeyListExistence = new(DiagnosticGroup.Localisation, 2);

    // ── Structure ──
    public static readonly DiagnosticId DeprecatedEventType = new(DiagnosticGroup.Structure, 1);
    public static readonly DiagnosticId DeprecatedTag = new(DiagnosticGroup.Structure, 2);
    public static readonly DiagnosticId EventTypeNotes = new(DiagnosticGroup.Structure, 3);
    public static readonly DiagnosticId StoryParamNotes = new(DiagnosticGroup.Structure, 4);
    public static readonly DiagnosticId TypeMismatch = new(DiagnosticGroup.Structure, 5);
    public static readonly DiagnosticId XmlDuplicateTag = new(DiagnosticGroup.Structure, 6);
    public static readonly DiagnosticId XmlNotes = new(DiagnosticGroup.Structure, 7);
    public static readonly DiagnosticId XmlStructure = new(DiagnosticGroup.Structure, 8);

    // Lua imports. Structure rather than a Lua-specific group: the group says what kind of problem
    // was reported, not which language reported it.
    public static readonly DiagnosticId LuaRedundantRequire = new(DiagnosticGroup.Structure, 9);
    public static readonly DiagnosticId LuaDuplicateRequire = new(DiagnosticGroup.Structure, 10);

    /// <summary>A dialog command that works but has not been verified against the engine.</summary>
    public static readonly DiagnosticId DialogUntestedCommand = new(DiagnosticGroup.Structure, 11);

    /// <summary>
    ///     An element written where a tag belongs that the schema has no tag by that name for, so
    ///     the engine reads and discards it.
    /// </summary>
    /// <remarks>
    ///     Its own id rather than a variant of <see cref="TypeMismatch" />: this one has to be
    ///     silenceable on its own. A mod may carry tags meant for an external tool, and the shipped
    ///     data itself is full of them - 458 distinct dead names over 3800 occurrences across foc/
    ///     and eaw/.
    /// </remarks>
    public static readonly DiagnosticId UnknownTag = new(DiagnosticGroup.Structure, 12);

    // ── CrossTag ──
    public static readonly DiagnosticId DamageNonzero = new(DiagnosticGroup.CrossTag, 1);
    public static readonly DiagnosticId DisallowedOrOperator = new(DiagnosticGroup.CrossTag, 2);
    public static readonly DiagnosticId HardpointAbilityNotOnOwner = new(DiagnosticGroup.CrossTag, 3);
    public static readonly DiagnosticId HardpointBoneNotOnModel = new(DiagnosticGroup.CrossTag, 4);
    public static readonly DiagnosticId HardpointMissingAttachmentBone = new(DiagnosticGroup.CrossTag, 5);
    public static readonly DiagnosticId HardpointModelBonesUnavailable = new(DiagnosticGroup.CrossTag, 6);
    public static readonly DiagnosticId PlanetModeExclusionList = new(DiagnosticGroup.CrossTag, 7);
    public static readonly DiagnosticId SquadronOffsetsMismatch = new(DiagnosticGroup.CrossTag, 8);

    public static readonly DiagnosticId PlanetModeMissingMode = new(DiagnosticGroup.CrossTag, 9);

    // 11, not 10: DamageStageNotOnModel already holds 10, further down this file.
    public static readonly DiagnosticId DamageAbsorbsNothing = new(DiagnosticGroup.CrossTag, 11);
    public static readonly DiagnosticId SpecialWeaponBehavior = new(DiagnosticGroup.CrossTag, 12);
    public static readonly DiagnosticId VehicleThiefCloneAbility = new(DiagnosticGroup.CrossTag, 13);

    /// <summary>
    ///     <c>Land_Damage_Thresholds</c> and <c>Land_Damage_Alternates</c> carry a different number
    ///     of entries, so the tail of the longer column pairs with nothing.
    /// </summary>
    /// <remarks>
    ///     Two columns, not the three the tag family has. <c>Land_Damage_SFX</c> disagrees with the
    ///     alternates on 42 of foc's 219 objects and 37 of eaw's 161, so an id covering all three
    ///     would be one the base game trips - see <c>LandDamageTableRule</c> for the measurement.
    /// </remarks>
    public static readonly DiagnosticId LandDamageTableMismatch = new(DiagnosticGroup.CrossTag, 14);

    public static readonly DiagnosticId MissingRequiredTag = new(DiagnosticGroup.CrossTag, 15);
    public static readonly DiagnosticId TagComparison = new(DiagnosticGroup.CrossTag, 16);

    /// <summary>
    ///     A flag switched on without the tag the engine requires beside it - "If you set
    ///     See_Fleet_Contents to true you must also set See_Num_Fleets to true", and five more.
    /// </summary>
    public static readonly DiagnosticId BooleanGatedRequirement = new(DiagnosticGroup.CrossTag, 17);

    /// <summary>
    ///     Both halves of an either/or flag pair are off, so the ability loads and does nothing -
    ///     "You should set either Can_Assassinate_Minor_Heroes or Can_Assassinate_Major_Heroes to
    ///     'Yes'", and two more.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="BooleanGatedRequirement" />: that one is a flag creating a
    ///     requirement the engine then REPAIRS, this one is an object the engine leaves inert. A
    ///     modder silencing "my ability deliberately covers neither case" must not thereby silence
    ///     "the engine is about to overwrite your value".
    /// </remarks>
    public static readonly DiagnosticId EitherOrRequirement = new(DiagnosticGroup.CrossTag, 18);

    /// <summary>
    ///     An ability with an automatic activation style that also causes its owner to despawn. The
    ///     engine states it once on the base class and then turns the flag off itself.
    /// </summary>
    public static readonly DiagnosticId AutomaticAbilityDespawn = new(DiagnosticGroup.CrossTag, 19);

    /// <summary>
    ///     A flag combination on an income stream the engine refuses and clears -
    ///     <c>Split_Favors_Owner</c> with no allies split, or alongside
    ///     <c>Full_Amount_To_Everyone</c>.
    /// </summary>
    /// <remarks>
    ///     One id for both, because they are one concern from the author's side: that flag set where
    ///     it cannot mean anything. Someone silencing one would mean the other.
    /// </remarks>
    public static readonly DiagnosticId IncomeSplitConflict = new(DiagnosticGroup.CrossTag, 20);

    /// <summary>
    ///     A destroyable hardpoint nothing can hit - no <c>Collision_Mesh</c>, or one another
    ///     destroyable hardpoint on the same object already claims.
    /// </summary>
    /// <remarks>
    ///     Derived from the damage-routing pass rather than from an engine message, which is why it
    ///     has its own id: someone who trusts their own routing and wants this quiet should not have
    ///     to silence the rules the engine itself states.
    /// </remarks>
    public static readonly DiagnosticId HardpointCannotBeHit = new(DiagnosticGroup.CrossTag, 21);

    /// <summary>
    ///     A system spy's <c>Duration_In_Secs</c> on the wrong side of zero for the
    ///     <c>Activation_Style</c> it declares - the engine overwrites it either way.
    /// </summary>
    /// <remarks>
    ///     One id for both arms of the branch. They are the same concern from the author's side -
    ///     a duration whose sign does not match the style - and someone silencing one would mean
    ///     the other.
    /// </remarks>
    public static readonly DiagnosticId SystemSpyDurationSign = new(DiagnosticGroup.CrossTag, 22);

    /// <summary>
    ///     A grenade or remote-bomb ability naming a projectile whose <c>Projectile_Category</c> is
    ///     not <c>GRENADE</c>, which the ability refuses.
    /// </summary>
    /// <remarks>
    ///     One id for both owners: it is one concern - the projectile is the wrong kind - and
    ///     someone silencing it for grenades would mean it for remote bombs.
    /// </remarks>
    public static readonly DiagnosticId GrenadeProjectileCategory = new(DiagnosticGroup.CrossTag, 23);

    /// <summary>
    ///     A <c>Vehicle_Thief_Inside_Clone</c> that keeps <c>GARRISON_VEHICLE</c>, which the tag's
    ///     description says the clone must not have.
    /// </summary>
    /// <remarks>
    ///     Its own id rather than <see cref="VehicleThiefCloneAbility" />: the ability half is an engine
    ///     assert, this half is the documented requirement plus a shared container, and two vanilla
    ///     clones trip it - someone silencing one should not lose the other.
    /// </remarks>
    public static readonly DiagnosticId VehicleThiefCloneGarrison = new(DiagnosticGroup.CrossTag, 24);

    /// <summary>
    ///     <c>Fires_Forward</c> on an object with no <c>WEAPON</c> behaviour, where nothing reads it.
    /// </summary>
    public static readonly DiagnosticId FiresForwardWithoutWeapon = new(DiagnosticGroup.CrossTag, 25);

    /// <summary>
    ///     <c>Fires_Forward</c> on a <c>WEAPON</c> object that also sets turret extents, which then no
    ///     longer limit the shot.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="FiresForwardWithoutWeapon" />: one flags a flag doing nothing, the
    ///     other two tags overruling each other on purpose, as vanilla's own bombing run does.
    /// </remarks>
    public static readonly DiagnosticId FiresForwardIgnoresArc = new(DiagnosticGroup.CrossTag, 26);

    /// <summary>
    ///     A unit without the WEAPON behaviour whose <c>Targeting_Max_Attack_Distance</c> exceeds the
    ///     longest <c>Fire_Range_Distance</c> of its weapon hardpoints, so it can stop where none fires
    ///     (#101).
    /// </summary>
    public static readonly DiagnosticId AttackDistanceBeyondHardpointRange = new(DiagnosticGroup.CrossTag, 27);

    /// <summary>
    ///     A WEAPON unit without the TURRET behaviour whose turret-extent tags restrict the shot - they are
    ///     the hull's firing arc, not turret configuration (A5). A hint: fighters do it on purpose.
    /// </summary>
    public static readonly DiagnosticId HullFiringArc = new(DiagnosticGroup.CrossTag, 28);

    /// <summary>
    ///     A faction's standalone special weapon names an object whose <c>Special_Weapon_Index</c> is unset
    ///     or outside 0 to 2, so the engine asserts and never registers the weapon (#98).
    /// </summary>
    public static readonly DiagnosticId SpecialWeaponIndex = new(DiagnosticGroup.CrossTag, 29);

    /// <summary>
    ///     <c>Specific_Death_Anim_Type</c> names a type the model has no clip for, or an index past its last
    ///     take, so no death animation plays - and with <c>Remove_Upon_Death</c> the unit vanishes at once (#104).
    /// </summary>
    public static readonly DiagnosticId DeathAnimationClip = new(DiagnosticGroup.CrossTag, 30);

    /// <summary>
    ///     <c>Land_Damage_Alternates</c> names a stage the object's model tags nothing for. Never the
    ///     reverse - see <see cref="PreviewDamageStageNotInModel" />, which is the same finding
    ///     reported inside the preview.
    /// </summary>
    public static readonly DiagnosticId DamageStageNotOnModel = new(DiagnosticGroup.CrossTag, 10);

    // ── Variants ──
    public static readonly DiagnosticId VariantAdditiveMerge = new(DiagnosticGroup.Variants, 1);
    public static readonly DiagnosticId VariantCycle = new(DiagnosticGroup.Variants, 2);
    public static readonly DiagnosticId VariantIgnoredOverride = new(DiagnosticGroup.Variants, 3);
    public static readonly DiagnosticId VariantRedundantOverride = new(DiagnosticGroup.Variants, 4);

    /// <summary>
    ///     A <c>Variant_Of_Existing_Type</c> naming a base that does not resolve. The engine ignores
    ///     the tag without a word, so the object loads as a blank slate.
    /// </summary>
    public static readonly DiagnosticId VariantBaseUnresolved = new(DiagnosticGroup.Variants, 5);

    /// <summary>
    ///     A base chain longer than the ten sweeps the engine gives variant resolution, which
    ///     therefore resolves or not depending on declaration order.
    /// </summary>
    public static readonly DiagnosticId VariantChainTooDeep = new(DiagnosticGroup.Variants, 6);

    /// <summary>
    ///     <c>Variant_Of_Existing_Type</c> on an object type that has no variant machinery, where the
    ///     engine parses the tag, fails to recognise it and logs an "Unprocessed entry" warning.
    /// </summary>
    public static readonly DiagnosticId VariantTagNotSupported = new(DiagnosticGroup.Variants, 7);

    // ── Story ──
    public static readonly DiagnosticId StoryChain = new(DiagnosticGroup.Story, 1);
    public static readonly DiagnosticId StoryGraph = new(DiagnosticGroup.Story, 2);
    public static readonly DiagnosticId CampaignStoryAttachment = new(DiagnosticGroup.Story, 3);
    public static readonly DiagnosticId StoryParamRequired = new(DiagnosticGroup.Story, 4);
    public static readonly DiagnosticId StoryParamUnknownSlot = new(DiagnosticGroup.Story, 5);
    public static readonly DiagnosticId StoryParamValue = new(DiagnosticGroup.Story, 6);
    public static readonly DiagnosticId CampaignStoryTupleInFactionTag = new(DiagnosticGroup.Story, 7);
    public static readonly DiagnosticId CampaignStoryMixedAuthoringForms = new(DiagnosticGroup.Story, 8);
    public static readonly DiagnosticId CampaignStoryConflictingAttachment = new(DiagnosticGroup.Story, 9);
    public static readonly DiagnosticId CampaignStoryRedundantAttachment = new(DiagnosticGroup.Story, 10);

    // ── Symbols ──
    public static readonly DiagnosticId DuplicateSymbol = new(DiagnosticGroup.Symbols, 1);
    public static readonly DiagnosticId CrossLayerShadow = new(DiagnosticGroup.Symbols, 2);
    public static readonly DiagnosticId CrossTypeShadow = new(DiagnosticGroup.Symbols, 3);
    public static readonly DiagnosticId UnnamedObject = new(DiagnosticGroup.Symbols, 4);

    // ── Engine ──
    // Built directly by XmlDiagnosticsPublisher rather than by a handler: these check values the
    // engine hardcodes, where the constraint is the engine's own table, not the schema's shape.
    public static readonly DiagnosticId DamageTypesIncomplete = new(DiagnosticGroup.Engine, 1);
    public static readonly DiagnosticId DamageTypesOutOfOrder = new(DiagnosticGroup.Engine, 2);
    public static readonly DiagnosticId UnknownHardcodedSetValue = new(DiagnosticGroup.Engine, 3);
    public static readonly DiagnosticId DeprecatedHardcodedSetValue = new(DiagnosticGroup.Engine, 4);

    // A local captured as an upvalue by a function the engine calls: an engine-hardcoded
    // expectation, the same kind of problem as the XML entries above.
    public static readonly DiagnosticId LuaEngineUpvalue = new(DiagnosticGroup.Engine, 5);

    // ── Syntax ──
    // Lua's parse errors come from Loretta already numbered and are mapped across by
    // LorettaDiagnosticIds, which owns 1-2999 of this group. Anything declared here must start at
    // LorettaDiagnosticIds.FirstOwnSyntaxNumber so the two can never collide.

    /// <summary>A line of a dialog script that does not parse.</summary>
    public static readonly DiagnosticId DialogParseError = new(DiagnosticGroup.Syntax, 3000);

    // ── Suppression ──
    // Suppression reporting on itself. These are the only diagnostics a user can silence into
    // silence about silencing, which is why they are worth having: without them a mistyped
    // directive looks exactly like a diagnostic that refuses to go away.

    /// <summary>An entry in a directive's rule list that is not an id or a group wildcard.</summary>
    public static readonly DiagnosticId SuppressionUnknownRule = new(DiagnosticGroup.Suppression, 1);

    /// <summary>A suppression directive that names no diagnostic at all.</summary>
    public static readonly DiagnosticId SuppressionNoRules = new(DiagnosticGroup.Suppression, 2);

    // ── Preview ──
    // What the model preview found while assembling a subject. Keyed by problem KIND rather than
    // by the site that reports it: two places report "this .alo would not parse", and a reader
    // silencing that is silencing one thing, not two.

    /// <summary>The subject asked for does not resolve to anything at all.</summary>
    public static readonly DiagnosticId PreviewSubjectNotFound = new(DiagnosticGroup.Preview, 1);

    /// <summary>A model file the subject names is not in the project or the game data.</summary>
    public static readonly DiagnosticId PreviewModelNotFound = new(DiagnosticGroup.Preview, 2);

    /// <summary>
    ///     A file that is neither an Alamo model nor a particle system. A renamed or truncated file
    ///     looks exactly like this.
    /// </summary>
    public static readonly DiagnosticId PreviewNotAModel = new(DiagnosticGroup.Preview, 3);

    /// <summary>A model that is there but could not be read.</summary>
    public static readonly DiagnosticId PreviewModelUnreadable = new(DiagnosticGroup.Preview, 4);

    /// <summary>The subject inherits in a cycle, so the assembled scene may be incomplete.</summary>
    public static readonly DiagnosticId PreviewInheritanceCycle = new(DiagnosticGroup.Preview, 5);

    /// <summary>The object declares no tactical model, so there is nothing to draw.</summary>
    public static readonly DiagnosticId PreviewNoTacticalModel = new(DiagnosticGroup.Preview, 6);

    /// <summary>A hardpoint is attached but defined nowhere.</summary>
    public static readonly DiagnosticId PreviewHardpointNotDefined = new(DiagnosticGroup.Preview, 7);

    /// <summary>A hardpoint's attached model is not there.</summary>
    public static readonly DiagnosticId PreviewHardpointModelNotFound =
        new(DiagnosticGroup.Preview, 8);

    /// <summary>A hardpoint names no attachment bone, so it sits at the hull's origin.</summary>
    public static readonly DiagnosticId PreviewHardpointNoBone = new(DiagnosticGroup.Preview, 9);

    /// <summary>A hardpoint attaches to a bone the hull does not have.</summary>
    public static readonly DiagnosticId PreviewHardpointBoneMissing =
        new(DiagnosticGroup.Preview, 10);

    /// <summary>A death clone is named by the object but defined nowhere.</summary>
    public static readonly DiagnosticId PreviewDeathCloneNotDefined =
        new(DiagnosticGroup.Preview, 11);

    /// <summary>A breakoff prop is named but defined nowhere, so the hardpoint vanishes.</summary>
    public static readonly DiagnosticId PreviewBreakoffPropNotDefined =
        new(DiagnosticGroup.Preview, 12);

    /// <summary>A projectile is fired by the object but defined nowhere.</summary>
    public static readonly DiagnosticId PreviewProjectileNotDefined =
        new(DiagnosticGroup.Preview, 13);

    /// <summary>An effect names an ability by its prefix that the object does not declare.</summary>
    public static readonly DiagnosticId PreviewUnboundAbilityEffect =
        new(DiagnosticGroup.Preview, 14);

    /// <summary>GameConstants maps no targeting reticle for a hardpoint type the object attaches.</summary>
    public static readonly DiagnosticId PreviewNoReticleForType = new(DiagnosticGroup.Preview, 15);

    /// <summary>
    ///     An animation override whose skeleton differs from the hull's. Informational: the
    ///     clips still play, bound by bone index.
    /// </summary>
    public static readonly DiagnosticId PreviewAnimationSkeletonMismatch =
        new(DiagnosticGroup.Preview, 16);

    /// <summary>A WEAPON behaviour whose model declares no MuzzleA bone to fire from.</summary>
    public static readonly DiagnosticId PreviewNoMuzzleBones = new(DiagnosticGroup.Preview, 17);

    /// <summary>An ability icon that is in neither the mega texture nor the loose icon sources.</summary>
    public static readonly DiagnosticId PreviewAbilityIconNotFound =
        new(DiagnosticGroup.Preview, 18);

    /// <summary>
    ///     A TARGETABLE hardpoint naming no <c>Attachment_Bone</c>, which the engine attaches to the
    ///     screen root - so it can never be hit, and the unit can never be destroyed.
    /// </summary>
    /// <remarks>
    ///     Its own id rather than a severity bump on
    ///     <see cref="PreviewHardpointNoBone" />: an author suppressing "this hardpoint is placed
    ///     sloppily" must not thereby silence "this unit cannot be killed".
    /// </remarks>
    public static readonly DiagnosticId PreviewTargetableHardpointNoBone =
        new(DiagnosticGroup.Preview, 19);

    /// <summary>
    ///     The object declares a damage stage in <c>Land_Damage_Alternates</c> that nothing in its
    ///     model is tagged for, so the stage exists but the unit does not change when it is reached.
    /// </summary>
    /// <remarks>
    ///     One direction only. A model MAY tag more stages than the XML uses - that is an asset
    ///     carrying more than this object asks of it, exactly like a <c>PTE_</c> effect on a unit
    ///     with no TURBO or a stealth shell on a unit with no cloak - and it is never reported.
    /// </remarks>
    public static readonly DiagnosticId PreviewDamageStageNotInModel =
        new(DiagnosticGroup.Preview, 20);

    /// <summary>
    ///     A destroyable hardpoint with NO <c>Collision_Mesh</c>, so no shot can ever reach it and the
    ///     unit can never be finished through its hardpoints.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>GameObjectClass::Take_Damage</c> finds a hardpoint by name, an exact
    ///         <c>_stricmp</c> against each <c>Collision_Mesh</c>. An empty name fails the size check
    ///         before that lookup (<c>00973ad9</c>) whether or not the hit was aimed, so the hardpoint
    ///         never dies and the all-destroyed branch - which counts by <c>Is_Destroyable</c> and never
    ///         asks whether a hardpoint was reachable - can never complete.
    ///     </para>
    ///     <para>
    ///         A value the MODEL lacks is NOT this. <c>Take_Damage</c> replaces the name with the
    ///         hardpoint's own value when the hit is aimed at it, so aimed fire still lands. That case
    ///         once shared this id, as an error; it is reported by
    ///         <see cref="HardpointBoneNotOnModel" /> instead, as a warning. Not reported where the
    ///         object does not die with its hardpoints - the palace keeps its generators as scenery on
    ///         purpose.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticId PreviewHardpointUnreachable =
        new(DiagnosticGroup.Preview, 21);
}