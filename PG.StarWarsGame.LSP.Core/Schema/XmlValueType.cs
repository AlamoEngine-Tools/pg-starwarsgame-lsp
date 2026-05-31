// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Schema;

/// <summary>Mirrors Petroglyph's internal XML value-type map.</summary>
public enum XmlValueType
{
    Boolean = 0,

    /// <summary>Small integer audio parameter (Priority, Pitch, Pan values); valid range roughly 0–140.</summary>
    AudioParamInt = 2,

    /// <summary>Small integer audio parameter (play count, max concurrent instances; -1 = unlimited).</summary>
    SfxCount = 3,

    /// <summary>Integer percentage 0–100 used for audio volume and probability fields.</summary>
    SfxPercentage = 4,

    /// <summary>Non-negative integer (tech levels, font sizes, frame counts).</summary>
    UInt = 5,

    /// <summary>General signed integer (credits, priorities, frame numbers).</summary>
    Int = 6,
    Float = 8,

    /// <summary>Floating-point value constrained to [0.0, 1.0] (fractions, multipliers, probabilities).</summary>
    NormalizedFloat = 9,

    /// <summary>Unsigned integer for hardware capability metrics (CPU MHz, texture memory MB, fill/vertex rates).</summary>
    HardwareUInt = 10,

    [Obsolete("No usages found in EaW XML data files — see docs/xml_type_analysis.md.")]
    Type11 = 11,

    /// <summary>Hex-encoded DirectX shader version string (e.g. "0x0200" = SM 2.0).</summary>
    ShaderVersionHex = 12,

    /// <summary>Hex-encoded GPU vendor ID string (e.g. "0x10DE" = NVIDIA).</summary>
    VendorIdHex = 13,
    DynamicEnumValue = 14,
    FloatVector2 = 15,
    FloatVector3 = 16,
    FloatVector4 = 17,
    IntList = 18,
    FloatList = 19,

    /// <summary>Space-separated list of 3D float vectors (e.g., Squadron_Offsets, Bombardment_Lighting_Color_List).</summary>
    FloatVector3List = 20,

    /// <summary>UV texture channel index (expected range 0–3).</summary>
    UvSlotIndex = 21,
    RGBA = 22,

    /// <summary>
    ///     Any string value — filenames, text IDs, and named-object references. Use referenceType in YAML to distinguish
    ///     actual object refs.
    /// </summary>
    NameReference = 23,

    [Obsolete("No usages found in EaW XML data files — see docs/xml_type_analysis.md.")]
    Type24 = 24,

    [Obsolete("No usages found in EaW XML data files — see docs/xml_type_analysis.md.")]
    Type25 = 25,

    [Obsolete("No usages found in EaW XML data files — see docs/xml_type_analysis.md.")]
    Type26 = 26,

    /// <summary>A space-separated list of named-object references. referenceType in YAML identifies the referenced pool.</summary>
    NameReferenceList = 27,

    /// <summary>Named position label string (e.g., "In_Base", "Out_Base", "Orbital").</summary>
    PositionLabel = 28,

    /// <summary>A strongly-typed single object reference; the engine knows exactly which pool to query.</summary>
    TypeReference = 29,

    /// <summary>A list of game-object names (space-separated). Typically unit/type names within one pool.</summary>
    GameObjectTypeReferenceList = 30,

    /// <summary>A reference to an SFXEvent by name.</summary>
    SFXEventReference = 31,

    /// <summary>A reference to a SpeechEvent by name.</summary>
    SpeechEventReference = 32,

    /// <summary>A reference to a MusicEvent by name.</summary>
    MusicEventReference = 33,

    /// <summary>Conditional SFX event override pair (unit type name + SFXEvent name).</summary>
    ConditionalSfxEvent = 34,
    Type35 = 35,
    Type36 = 36,
    Type37 = 37,
    Type38 = 38,

    /// <summary>A reference to an SFXEvent used for HUD feedback (special-weapon state changes, etc.).</summary>
    SfxEventHudReference = 39,

    /// <summary>Conditional SpeechEvent intro trigger with Or/And logic (unit type conditions + SpeechEvent name).</summary>
    ConditionalSpeechEvent = 40,

    /// <summary>Per-faction music event map.</summary>
    [Obsolete("No usages found in EaW XML data files — see docs/xml_type_analysis.md.")]
    MusicEventPerFactionMap = 41,

    /// <summary>A space-separated list of type references.</summary>
    TypeReferenceList = 42,

    /// <summary>Per-faction scalar value (e.g. starting credits per faction).</summary>
    PerFactionValue = 43,

    [Obsolete("No usages found in EaW XML data files — see docs/xml_type_analysis.md.")]
    Type44 = 44,

    /// <summary>Per-faction planet reference pair ("FactionName, PlanetName").</summary>
    PerFactionPlanet = 45,

    [Obsolete("No usages found in EaW XML data files — see docs/xml_type_analysis.md.")]
    Type46 = 46,
    FloatTupleList = 47,
    IntFloatTupleList = 48,

    [Obsolete("No usages found in EaW XML data files — see docs/xml_type_analysis.md.")]
    Type49 = 49,

    /// <summary>Ship class enum value (ShipClass, not the generic DynamicEnumValue).</summary>
    ShipClassType = 50,

    [Obsolete("No usages found in EaW XML data files — see docs/xml_type_analysis.md.")]
    Type51 = 51,

    /// <summary>Weighted list of music events.</summary>
    MusicEventWeightedList = 52,

    /// <summary>Per-faction object list pair (faction name + space-separated object names).</summary>
    PerFactionObjectList = 53,

    [Obsolete("No usages found in EaW XML data files — see docs/xml_type_analysis.md.")]
    ShipNameTextFileList = 54,

    /// <summary>Campaign force deployment tuple (faction name, planet name, unit type name).</summary>
    ForceDeploymentList = 55,

    /// <summary>
    ///     Ability sub-object list (engine internal: AbilitySubObjectList). Contains heterogeneous named child elements
    ///     whose tag name is the ability class (snake_case → PascalCase schema type, e.g. Lucky_Shot_Attack_Ability →
    ///     LuckyShotAttackAbility). Each child's Name attribute is indexed as a GameSymbol.
    /// </summary>
    AbilityDefinitionSubObjectList = 56,

    /// <summary>
    ///     Unit abilities data sub-object list (engine internal: UnitAbilitiesDataSubObjectList). Contains anonymous
    ///     Unit_Ability elements that activate named abilities at runtime. GUI_Activated_Ability_Name cross-references
    ///     a named ability from the AbilityDefinitionSubObjectList Abilities list.
    /// </summary>
    GuiActivatedAbilityDefinitionSubObjectList = 57,

    [Obsolete("No usages found in EaW XML data files — see docs/xml_type_analysis.md.")]
    Type58 = 58,

    /// <summary>Death clone specification (condition + type pair).</summary>
    DeathCloneSpec = 59,

    /// <summary>Per-faction integer map (e.g. maximum political control by faction).</summary>
    PerFactionIntMap = 60,

    /// <summary>Inaccuracy distance map (category, float pairs).</summary>
    InaccuracyMap = 61,
    DamageToArmorMod = 62,

    /// <summary>Audio 3D provider name string (quoted DirectSound3D provider name, e.g., "EAX").</summary>
    Audio3dProviderName = 63,
    LocalisationToTextureMap = 64,
    HardPointTypeToTextureMap = 65,

    /// <summary>
    ///     Comma-separated (HardPointType, SFXEvent name) tuple. Maps a hard-point slot to an SFXEvent; multiple entries
    ///     per tag. Empty SFXEvent name is valid (slot declared, no sound).
    /// </summary>
    HardPointSfxMap = 66,

    /// <summary>Faction name reference (single faction identifier string).</summary>
    FactionReference = 67,

    [Obsolete("No usages found in EaW XML data files — see docs/xml_type_analysis.md.")]
    Type68 = 68,

    [Obsolete("No usages found in EaW XML data files — see docs/xml_type_analysis.md.")]
    Type69 = 69,

    /// <summary>
    ///     Comma-separated (unit type name, int count) tuple per tech level. Count of -1 means unlimited/default stack
    ///     size. Multiple entries per tag define multi-unit spawn sets.
    /// </summary>
    UnitSpawnTable = 70,

    /// <summary>
    ///     Comma-separated (unit type name, float probability) tuple. Used for destruction survivor and map-load spawn
    ///     tables.
    /// </summary>
    UnitSpawnProbabilityTable = 71,

    [Obsolete("No usages found in EaW XML data files — see docs/xml_type_analysis.md.")]
    CategoryToIntegerMap = 72,

    /// <summary>
    ///     Ability behaviour type identifier (engine internal: AbilityType C++ enum). Names the engine behaviour
    ///     associated with a SpecialAbilityData entry (e.g. HUNT, FORCE_CLOAK, DEFEND).
    ///     Values overlap with BehaviorModule names but are a separate enum — do not conflate them.
    /// </summary>
    AbilityType = 73,

    /// <summary>Projectile category enum name (e.g. a named combat category used for targeting logic).</summary>
    ProjectileCategory = 74,

    /// <summary>Cable-attack render mode enum name.</summary>
    CableRenderMode = 75,

    /// <summary>
    ///     Comma-separated (ability name, SFXEvent name) tuple for GUI ability toggle feedback. Empty SFXEvent name is
    ///     valid (ability declared, no toggle sound).
    /// </summary>
    AbilitySfxMap = 76,

    /// <summary>
    ///     Comma-separated tuple of (AbilityMultiplierType C++ enum name, float multiplier). Applied per
    ///     SpecialAbilityData Mod_Multiplier entry; multiple entries allowed per ability.
    /// </summary>
    AbilityModMultiplier = 77,

    /// <summary>
    ///     Comma-separated tuple of (AbilityFlagType C++ enum name, bool). Sets a named boolean modifier flag on the
    ///     ability.
    /// </summary>
    AbilityModFlag = 78,

    /// <summary>BinkMovie frame-event tuple (frame number + event name or script action).</summary>
    MovieFrameTrigger = 79,

    /// <summary>CommandBarComponent boolean/numeric GUI property (property name + value pair).</summary>
    CommandBarProperty = 80,

    [Obsolete("No usages found in EaW XML data files — see docs/xml_type_analysis.md.")]
    Type81 = 81,

    [Obsolete("No usages found in EaW XML data files — see docs/xml_type_analysis.md.")]
    Type82 = 82
}