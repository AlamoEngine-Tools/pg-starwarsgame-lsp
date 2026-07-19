// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml;

internal static class ValueTypeHint
{
    public static string? Build(XmlTagDefinition tag)
    {
        return tag.ValueType switch
        {
            XmlValueType.Float =>
                "**Format:** `1`, `1.0`, or `1.0f`",

            XmlValueType.NormalizedFloat =>
                "**Format:** float in `[0.0, 1.0]` - e.g. `0.5`, `0.75f`",

            XmlValueType.Boolean =>
                "**Format:** `True`, `False`, `Yes`, `No`, `1`, or `0`",

            XmlValueType.SfxCount =>
                "**Format:** integer; `-1` = unlimited",

            XmlValueType.SfxPercentage =>
                "**Format:** integer `0`–`100`",

            XmlValueType.HardwareUInt =>
                "**Format:** unsigned integer",

            XmlValueType.UvSlotIndex =>
                "**Format:** integer `0`–`3`",

            XmlValueType.ShaderVersionHex =>
                "**Format:** hex string, e.g. `0x0200` (= Shader Model 2.0)",

            XmlValueType.VendorIdHex =>
                "**Format:** hex string GPU vendor ID, e.g. `0x10DE` (NVIDIA)",

            XmlValueType.FloatVector2 =>
                "**Format:** `X, Y` - two comma-separated floats",

            XmlValueType.FloatVector3 =>
                "**Format:** `X, Y, Z` - three comma-separated floats",

            XmlValueType.FloatVector4 =>
                "**Format:** `X, Y, Z, W` - four comma-separated floats",

            XmlValueType.RGBA =>
                "**Format:** `R, G, B, A` - four integers `0`–`255`",

            XmlValueType.IntList =>
                "**Format:** comma or space-separated integers",

            XmlValueType.FloatList =>
                "**Format:** comma or space-separated floats (`1`, `1.0`, `1.0f`)",

            XmlValueType.FloatTupleList =>
                "**Format:** comma-separated float pairs",

            XmlValueType.IntFloatTupleList =>
                "**Format:** comma-separated `(int, float)` pairs",

            XmlValueType.NameReference => BuildNameReferenceHint(tag, false),
            XmlValueType.NameReferenceList => BuildNameReferenceHint(tag, true),

            XmlValueType.TypeReference =>
                "**Reference:** name of a single game object type",

            XmlValueType.TypeReferenceList or XmlValueType.GameObjectTypeReferenceList =>
                "**Reference:** space-separated game object type names",

            XmlValueType.SFXEventReference =>
                "**Reference:** name of an `SFXEvent`",

            XmlValueType.SpeechEventReference =>
                "**Reference:** name of a `SpeechEvent`",

            XmlValueType.MusicEventReference =>
                "**Reference:** name of a `MusicEvent`",

            XmlValueType.SfxEventHudReference =>
                "**Reference:** name of an `SFXEvent` (HUD feedback)",

            XmlValueType.DynamicEnumValue => BuildEnumHint(tag),

            XmlValueType.ShipClassType =>
                "**Enum:** ship class - e.g. `Infantry`, `Corvette`, `Frigate`, `Capital`",

            XmlValueType.PerFactionValue =>
                "**Format:** `FactionName, value` pairs",

            XmlValueType.PerFactionIntMap =>
                "**Format:** `FactionName, integer` pairs",

            XmlValueType.DamageToArmorMod =>
                "**Format:** `DamageType, ArmorType, modifier` groups",

            XmlValueType.InaccuracyMap =>
                "**Format:** `category, float` accuracy modifier pairs",

            XmlValueType.LocalisationToTextureMap =>
                "**Format:** `locale, texturePath` pairs",

            XmlValueType.HardPointTypeToTextureMap =>
                "**Format:** `HardPointType, texturePath` pairs",

            XmlValueType.HardPointSfxMap =>
                "**Format:** `HardPointType, SFXEventName` pairs (empty name = no sound)",

            XmlValueType.AbilitySfxMap =>
                "**Format:** `AbilityName, SFXEventName` pairs (empty name = no sound)",

            XmlValueType.AbilityModMultiplier =>
                "**Format:** `AbilityMultiplierType, float` tuples",

            XmlValueType.AbilityModFlag =>
                "**Format:** `AbilityFlagType, True|False` tuples",

            XmlValueType.UnitSpawnTable =>
                "**Format:** `UnitType, count` pairs; `-1` = unlimited",

            XmlValueType.UnitSpawnProbabilityTable =>
                "**Format:** `UnitType, probability` pairs",

            XmlValueType.TupleList =>
                "**Format:** `MusicEventName, weight` pairs",

            XmlValueType.DeathCloneSpec =>
                "**Format:** `condition, UnitType` pairs",

            _ => null
        };
    }

    private static string? BuildNameReferenceHint(XmlTagDefinition tag, bool plural)
    {
        var sep = plural ? "space-separated list of " : string.Empty;
        var suffix = plural ? " names" : string.Empty;
        return tag.ReferenceKind switch
        {
            ReferenceKind.XmlObject when tag.ObjectType is not null =>
                $"**Reference:** {sep}`{tag.ObjectType.TypeName}`{suffix}",

            ReferenceKind.XmlObject =>
                $"**Reference:** {sep}XML object name{(plural ? "s" : string.Empty)}",

            ReferenceKind.ModelFile =>
                plural
                    ? "**Reference:** space-separated list of `.alo` model filenames"
                    : "**Reference:** `.alo` 3D model filename",

            ReferenceKind.TextureFile =>
                plural
                    ? "**Reference:** space-separated list of `.tga` / `.dds` texture filenames"
                    : "**Reference:** `.tga` or `.dds` texture filename",

            ReferenceKind.AudioFile =>
                plural
                    ? "**Reference:** space-separated list of audio sample filenames"
                    : "**Reference:** audio sample filename",

            ReferenceKind.BoneName =>
                plural
                    ? "**Reference:** space-separated bone names from the model skeleton"
                    : "**Reference:** bone name from the model skeleton",

            ReferenceKind.LocalisationKey =>
                plural
                    ? "**Reference:** space-separated localisation string keys (`TEXT_xxx`)"
                    : "**Reference:** localisation string key (`TEXT_xxx` format)",

            ReferenceKind.Enum when tag.Enum is not null =>
                plural
                    ? $"**Enum:** space-separated values from `{tag.Enum.Name}`"
                    : $"**Enum:** one of the values in `{tag.Enum.Name}`",

            ReferenceKind.Enum =>
                plural
                    ? "**Enum:** space-separated enum values"
                    : "**Enum:** predefined enum value",

            _ => null
        };
    }

    private static string BuildEnumHint(XmlTagDefinition tag)
    {
        return tag.Enum is not null
            ? $"**Enum:** one of the values in `{tag.Enum.Name}`"
            : "**Enum:** predefined enum value";
    }
}