// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Xml.Tests;

public sealed class ValueTypeHintTest
{
    private static XmlTagDefinition Tag(
        XmlValueType type,
        ReferenceKind referenceKind = ReferenceKind.None,
        string? referenceType = null,
        string? enumName = null)
    {
        return new XmlTagDefinition
        {
            Tag = "Test",
            ValueType = type,
            ReferenceKind = referenceKind,
            ReferenceType = referenceType,
            EnumName = enumName
        };
    }

    // ── scalar types ────────────────────────────────────────────────────────

    [Fact]
    public void Float_ContainsSuffixExample()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.Float));
        Assert.NotNull(hint);
        Assert.Contains("1.0f", hint);
    }

    [Fact]
    public void NormalizedFloat_HintContainsRange()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.NormalizedFloat));
        Assert.NotNull(hint);
        Assert.Contains("0.0", hint);
        Assert.Contains("1.0", hint);
    }

    [Fact]
    public void Boolean_ContainsTrueAndZero()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.Boolean));
        Assert.NotNull(hint);
        Assert.Contains("True", hint);
        Assert.Contains("0", hint);
    }

    [Fact]
    public void SfxCount_ContainsUnlimited()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.SfxCount));
        Assert.NotNull(hint);
        Assert.Contains("-1", hint);
        Assert.Contains("unlimited", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SfxPercentage_ContainsRange()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.SfxPercentage));
        Assert.NotNull(hint);
        Assert.Contains("0", hint);
        Assert.Contains("100", hint);
    }

    [Fact]
    public void UvSlotIndex_ContainsRange()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.UvSlotIndex));
        Assert.NotNull(hint);
        Assert.Contains("0", hint);
        Assert.Contains("3", hint);
    }

    [Fact]
    public void ShaderVersionHex_ContainsExample()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.ShaderVersionHex));
        Assert.NotNull(hint);
        Assert.Contains("0x0200", hint);
    }

    [Fact]
    public void VendorIdHex_ContainsExample()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.VendorIdHex));
        Assert.NotNull(hint);
        Assert.Contains("0x10DE", hint);
    }

    // ── vector types ─────────────────────────────────────────────────────────

    [Fact]
    public void FloatVector2_ContainsXY()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.FloatVector2));
        Assert.NotNull(hint);
        Assert.Contains("X, Y", hint);
    }

    [Fact]
    public void FloatVector3_ContainsXYZ()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.FloatVector3));
        Assert.NotNull(hint);
        Assert.Contains("X, Y, Z", hint);
    }

    [Fact]
    public void FloatVector4_ContainsXYZW()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.FloatVector4));
        Assert.NotNull(hint);
        Assert.Contains("X, Y, Z, W", hint);
    }

    [Fact]
    public void RGBA_ContainsByteRange()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.RGBA));
        Assert.NotNull(hint);
        Assert.Contains("255", hint);
    }

    // ── list types ───────────────────────────────────────────────────────────

    [Fact]
    public void IntList_MentionsSeparators()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.IntList));
        Assert.NotNull(hint);
        Assert.Contains("comma or space", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FloatList_MentionsSuffixAndSeparators()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.FloatList));
        Assert.NotNull(hint);
        Assert.Contains("1.0f", hint);
    }

    // ── reference types — NameReference ─────────────────────────────────────

    [Fact]
    public void NameReference_XmlObject_WithReferenceType_ContainsTypeName()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.NameReference,
            ReferenceKind.XmlObject, "Faction"));
        Assert.NotNull(hint);
        Assert.Contains("Faction", hint);
        Assert.DoesNotContain("space-separated", hint);
    }

    [Fact]
    public void NameReference_XmlObject_NoReferenceType_Generic()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.NameReference, ReferenceKind.XmlObject));
        Assert.NotNull(hint);
        Assert.Contains("XML object", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NameReference_ModelFile_ContainsAlo()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.NameReference, ReferenceKind.ModelFile));
        Assert.NotNull(hint);
        Assert.Contains(".alo", hint);
    }

    [Fact]
    public void NameReference_TextureFile_ContainsTga()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.NameReference, ReferenceKind.TextureFile));
        Assert.NotNull(hint);
        Assert.Contains(".tga", hint);
    }

    [Fact]
    public void NameReference_AudioFile_MentionsAudio()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.NameReference, ReferenceKind.AudioFile));
        Assert.NotNull(hint);
        Assert.Contains("audio", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NameReference_BoneName_MentionsBone()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.NameReference, ReferenceKind.BoneName));
        Assert.NotNull(hint);
        Assert.Contains("bone", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NameReference_LocalisationKey_ContainsTEXT()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.NameReference, ReferenceKind.LocalisationKey));
        Assert.NotNull(hint);
        Assert.Contains("TEXT_xxx", hint);
    }

    [Fact]
    public void NameReference_Enum_WithEnumName_ContainsEnumName()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.NameReference,
            ReferenceKind.Enum, enumName: "ArmorType"));
        Assert.NotNull(hint);
        Assert.Contains("ArmorType", hint);
    }

    [Fact]
    public void NameReference_None_ReturnsNull()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.NameReference));
        Assert.Null(hint);
    }

    [Fact]
    public void NameReference_Unknown_ReturnsNull()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.NameReference, ReferenceKind.Unknown));
        Assert.Null(hint);
    }

    // ── reference types — NameReferenceList ──────────────────────────────────

    [Fact]
    public void NameReferenceList_XmlObject_WithReferenceType_ContainsSpaceSeparatedAndTypeName()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.NameReferenceList,
            ReferenceKind.XmlObject, "SFXEvent"));
        Assert.NotNull(hint);
        Assert.Contains("space-separated", hint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SFXEvent", hint);
    }

    [Fact]
    public void NameReferenceList_ModelFile_MentionsList()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.NameReferenceList, ReferenceKind.ModelFile));
        Assert.NotNull(hint);
        Assert.Contains(".alo", hint);
        Assert.Contains("space-separated", hint, StringComparison.OrdinalIgnoreCase);
    }

    // ── unambiguous reference types ───────────────────────────────────────────

    [Fact]
    public void SFXEventReference_MentionsSFXEvent()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.SFXEventReference));
        Assert.NotNull(hint);
        Assert.Contains("SFXEvent", hint);
    }

    [Fact]
    public void SpeechEventReference_MentionsSpeechEvent()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.SpeechEventReference));
        Assert.NotNull(hint);
        Assert.Contains("SpeechEvent", hint);
    }

    [Fact]
    public void MusicEventReference_MentionsMusicEvent()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.MusicEventReference));
        Assert.NotNull(hint);
        Assert.Contains("MusicEvent", hint);
    }

    [Fact]
    public void TypeReference_MentionsGameObjectType()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.TypeReference));
        Assert.NotNull(hint);
        Assert.Contains("game object type", hint, StringComparison.OrdinalIgnoreCase);
    }

    // ── enum types ────────────────────────────────────────────────────────────

    [Fact]
    public void DynamicEnumValue_WithEnumName_ContainsEnumName()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.DynamicEnumValue,
            enumName: "GameObjectCategoryType"));
        Assert.NotNull(hint);
        Assert.Contains("GameObjectCategoryType", hint);
    }

    [Fact]
    public void DynamicEnumValue_NoEnumName_ReturnsFallback()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.DynamicEnumValue));
        Assert.NotNull(hint);
        Assert.Contains("enum", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShipClassType_MentionsShipClasses()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.ShipClassType));
        Assert.NotNull(hint);
        Assert.Contains("Corvette", hint);
    }

    // ── tuple / map types ────────────────────────────────────────────────────

    [Fact]
    public void UnitSpawnTable_ContainsUnlimited()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.UnitSpawnTable));
        Assert.NotNull(hint);
        Assert.Contains("-1", hint);
        Assert.Contains("unlimited", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HardPointSfxMap_MentionsEmptyName()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.HardPointSfxMap));
        Assert.NotNull(hint);
        Assert.Contains("empty", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AbilityModFlag_MentionsBooleanValues()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.AbilityModFlag));
        Assert.NotNull(hint);
        Assert.Contains("True", hint);
        Assert.Contains("False", hint);
    }

    // ── unknown / unnamed types ───────────────────────────────────────────────

    [Fact]
    public void FloatVector3List_ReturnsNull()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.FloatVector3List));
        Assert.Null(hint);
    }

    [Fact]
    public void AudioParamInt_ReturnsNull()
    {
        var hint = ValueTypeHint.Build(Tag(XmlValueType.AudioParamInt));
        Assert.Null(hint);
    }
}