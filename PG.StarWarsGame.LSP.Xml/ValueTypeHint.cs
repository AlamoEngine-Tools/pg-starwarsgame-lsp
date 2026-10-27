// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Messages;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml;

internal static class ValueTypeHint
{
    public static string? Build(XmlTagDefinition tag, IMessageCatalog messages)
    {
        return tag.ValueType switch
        {
            XmlValueType.Float =>
                messages.Format("ValueTypeHint_FormatF"),

            XmlValueType.NormalizedFloat =>
                messages.Format("ValueTypeHint_FormatFloatEGF"),

            XmlValueType.Boolean =>
                messages.Format("ValueTypeHint_FormatTrueFalseYesNo"),

            XmlValueType.SfxCount =>
                messages.Format("ValueTypeHint_FormatIntegerUnlimited"),

            XmlValueType.SfxPercentage =>
                messages.Format("ValueTypeHint_FormatInteger"),

            XmlValueType.HardwareUInt =>
                messages.Format("ValueTypeHint_FormatUnsignedInteger"),

            XmlValueType.UvSlotIndex =>
                messages.Format("ValueTypeHint_FormatInteger2"),

            XmlValueType.ShaderVersionHex =>
                messages.Format("ValueTypeHint_FormatHexStringEG"),

            XmlValueType.VendorIdHex =>
                messages.Format("ValueTypeHint_FormatHexStringGPUVendor"),

            XmlValueType.FloatVector2 =>
                messages.Format("ValueTypeHint_FormatXYTwoComma"),

            XmlValueType.FloatVector3 =>
                messages.Format("ValueTypeHint_FormatXYZThree"),

            XmlValueType.FloatVector4 =>
                messages.Format("ValueTypeHint_FormatXYZW"),

            XmlValueType.RGBA =>
                messages.Format("ValueTypeHint_FormatRGBFour"),

            XmlValueType.IntList =>
                messages.Format("ValueTypeHint_FormatCommaSpaceSeparatedIntegers"),

            XmlValueType.FloatList =>
                messages.Format("ValueTypeHint_FormatCommaSpaceSeparatedFloats"),

            XmlValueType.FloatTupleList =>
                messages.Format("ValueTypeHint_FormatCommaSeparatedFloatPairs"),

            XmlValueType.IntFloatTupleList =>
                messages.Format("ValueTypeHint_FormatCommaSeparatedIntFloat"),

            XmlValueType.NameReference => BuildNameReferenceHint(tag, false, messages),
            XmlValueType.NameReferenceList => BuildNameReferenceHint(tag, true, messages),

            XmlValueType.TypeReference =>
                messages.Format("ValueTypeHint_ReferenceNameSingleGameObject"),

            XmlValueType.TypeReferenceList or XmlValueType.GameObjectTypeReferenceList =>
                messages.Format("ValueTypeHint_ReferenceSpaceSeparatedGameObject"),

            XmlValueType.SFXEventReference =>
                messages.Format("ValueTypeHint_ReferenceNameSFXEvent"),

            XmlValueType.SpeechEventReference =>
                messages.Format("ValueTypeHint_ReferenceNameSpeechEvent"),

            XmlValueType.MusicEventReference =>
                messages.Format("ValueTypeHint_ReferenceNameMusicEvent"),

            XmlValueType.SfxEventHudReference =>
                messages.Format("ValueTypeHint_ReferenceNameSFXEventHUDFeedback"),

            XmlValueType.DynamicEnumValue => BuildEnumHint(tag, messages),

            XmlValueType.ShipClassType =>
                messages.Format("ValueTypeHint_EnumShipClassEG"),

            XmlValueType.PerFactionValue =>
                messages.Format("ValueTypeHint_FormatFactionNameValuePairs"),

            XmlValueType.PerFactionIntMap =>
                messages.Format("ValueTypeHint_FormatFactionNameIntegerPairs"),

            XmlValueType.DamageToArmorMod =>
                messages.Format("ValueTypeHint_FormatDamageTypeArmorTypeModifierGroups"),

            XmlValueType.InaccuracyMap =>
                messages.Format("ValueTypeHint_FormatCategoryFloatAccuracyModifier"),

            XmlValueType.LocalisationToTextureMap =>
                messages.Format("ValueTypeHint_FormatLocaleTexturePathPairs"),

            XmlValueType.HardPointTypeToTextureMap =>
                messages.Format("ValueTypeHint_FormatHardPointTypeTexturePathPairs"),

            XmlValueType.HardPointSfxMap =>
                messages.Format("ValueTypeHint_FormatHardPointTypeSFXEventNamePairsEmpty"),

            XmlValueType.AbilitySfxMap =>
                messages.Format("ValueTypeHint_FormatAbilityNameSFXEventNamePairsEmpty"),

            XmlValueType.AbilityModMultiplier =>
                messages.Format("ValueTypeHint_FormatAbilityMultiplierTypeFloatTuples"),

            XmlValueType.AbilityModFlag =>
                messages.Format("ValueTypeHint_FormatAbilityFlagTypeTrueFalseTuples"),

            XmlValueType.UnitSpawnTable =>
                messages.Format("ValueTypeHint_FormatUnitTypeCountPairsUnlimited"),

            XmlValueType.UnitSpawnProbabilityTable =>
                messages.Format("ValueTypeHint_FormatUnitTypeProbabilityPairs"),

            // What the items are belongs to the tag's slots, not to the type: TupleList is the
            // engine's generic pair list, used for music events AND for terrain/model pairs, and a
            // ListMap maps factions to objects AND animation states to units.
            XmlValueType.TupleList or XmlValueType.ListMap =>
                messages.Format("ValueTypeHint_FormatTuple", TupleItems.Describe(tag, messages)),

            XmlValueType.DeathCloneSpec =>
                messages.Format("ValueTypeHint_FormatConditionUnitTypePairs"),

            _ => null
        };
    }

    private static string? BuildNameReferenceHint(XmlTagDefinition tag, bool plural, IMessageCatalog messages)
    {
        return tag.ReferenceKind switch
        {
            ReferenceKind.XmlObject when tag.ObjectType is not null =>
                plural
                    ? messages.Format("ValueTypeHint_ObjectTypeNames", tag.ObjectType.TypeName)
                    : messages.Format("ValueTypeHint_ObjectTypeName", tag.ObjectType.TypeName),

            ReferenceKind.XmlObject =>
                plural
                    ? messages.Format("ValueTypeHint_XmlObjectNames")
                    : messages.Format("ValueTypeHint_XmlObjectName"),

            ReferenceKind.ModelFile =>
                plural
                    ? messages.Format("ValueTypeHint_ReferenceSpaceSeparatedListAlo")
                    : messages.Format("ValueTypeHint_ReferenceAloDModelFilename"),

            ReferenceKind.TextureFile =>
                plural
                    ? messages.Format("ValueTypeHint_ReferenceSpaceSeparatedListTga")
                    : messages.Format("ValueTypeHint_ReferenceTgaDdsTextureFilename"),

            ReferenceKind.AudioFile =>
                plural
                    ? messages.Format("ValueTypeHint_ReferenceSpaceSeparatedListAudio")
                    : messages.Format("ValueTypeHint_ReferenceAudioSampleFilename"),

            ReferenceKind.BoneName =>
                plural
                    ? messages.Format("ValueTypeHint_ReferenceSpaceSeparatedBoneNames")
                    : messages.Format("ValueTypeHint_ReferenceBoneNameFromModel"),

            ReferenceKind.LocalisationKey =>
                plural
                    ? messages.Format("ValueTypeHint_ReferenceSpaceSeparatedLocalisationString")
                    : messages.Format("ValueTypeHint_ReferenceLocalisationStringKeyTEXT"),

            ReferenceKind.Enum when tag.Enum is not null =>
                plural
                    ? messages.Format("ValueTypeHint_EnumSpaceSeparatedValuesFrom", tag.Enum.Name)
                    : messages.Format("ValueTypeHint_EnumOneValues", tag.Enum.Name),

            ReferenceKind.Enum =>
                plural
                    ? messages.Format("ValueTypeHint_EnumSpaceSeparatedEnumValues")
                    : messages.Format("ValueTypeHint_EnumPredefinedEnumValue"),

            _ => null
        };
    }

    private static string BuildEnumHint(XmlTagDefinition tag, IMessageCatalog messages)
    {
        return tag.Enum is not null
            ? messages.Format("ValueTypeHint_EnumOneValues", tag.Enum.Name)
            : messages.Format("ValueTypeHint_EnumPredefinedEnumValue");
    }
}