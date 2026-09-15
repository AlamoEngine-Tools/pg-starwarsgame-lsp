// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;

namespace PG.StarWarsGame.LSP.Core.Tests.Diagnostics;

/// <summary>
///     Which value types the 8192-byte value limit actually applies to.
/// </summary>
/// <remarks>
///     <para>
///         <c>DatabaseMapClass::Map_Data_Of_Type</c> is one switch on the type code
///         (<c>CMP 0x52; JA default; JMP [ECX*4 + 0xcbbed4]</c>, 83 entries), so the
///         <c>strtok_string_buffer</c> copy belongs to individual CASES rather than to the function.
///         Mapping all 50 assert sites through that jump table gives 50 of the 83 codes, every one
///         of them a composite - the vectors, lists, tuple lists, every per-faction type and every
///         map.
///     </para>
///     <para>
///         The 33 without it are the scalars and single references, which are read straight out of
///         the node and never copied into the buffer. Reporting the limit on those is wider than the
///         engine, which is the one thing this repo's rules are not allowed to be.
///     </para>
///     <para>
///         <c>(int)XmlValueType</c> IS the engine type code - <c>FloatVector2 = 15</c> is
///         <c>0x0f</c>, <c>AbilityModFlag = 78</c> is <c>0x4e</c> - so the set is expressed in codes
///         and needs no hand-written list of names to drift out of date.
///     </para>
/// </remarks>
public sealed class EngineTextLimitsScopeTest
{
    /// <summary>Scalars and single references: read from the node, never copied.</summary>
    [Theory]
    [InlineData(XmlValueType.Boolean)]
    [InlineData(XmlValueType.Int)]
    [InlineData(XmlValueType.UInt)]
    [InlineData(XmlValueType.Float)]
    [InlineData(XmlValueType.NormalizedFloat)]
    [InlineData(XmlValueType.DynamicEnumValue)]
    [InlineData(XmlValueType.NameReference)]
    [InlineData(XmlValueType.TypeReference)]
    [InlineData(XmlValueType.SFXEventReference)]
    [InlineData(XmlValueType.SpeechEventReference)]
    [InlineData(XmlValueType.MusicEventReference)]
    [InlineData(XmlValueType.FactionReference)]
    [InlineData(XmlValueType.ProjectileCategory)]
    [InlineData(XmlValueType.CommandBarProperty)]
    public void A_scalar_or_single_reference_is_never_copied_into_the_buffer(XmlValueType type)
    {
        Assert.False(EngineTextLimits.ValueIsCopiedToBuffer(type));
    }

    /// <summary>Composites: strtok'd through the fixed buffer, so the limit is real.</summary>
    [Theory]
    [InlineData(XmlValueType.FloatVector2)]
    [InlineData(XmlValueType.FloatVector3)]
    [InlineData(XmlValueType.IntList)]
    [InlineData(XmlValueType.FloatList)]
    [InlineData(XmlValueType.RGBA)]
    [InlineData(XmlValueType.NameReferenceList)]
    [InlineData(XmlValueType.GameObjectTypeReferenceList)]
    [InlineData(XmlValueType.TypeReferenceList)]
    [InlineData(XmlValueType.TupleList)]
    [InlineData(XmlValueType.FloatTupleList)]
    [InlineData(XmlValueType.PerFactionObjectList)]
    [InlineData(XmlValueType.UnitSpawnTable)]
    [InlineData(XmlValueType.HardPointSfxMap)]
    [InlineData(XmlValueType.AbilityModFlag)]
    [InlineData(XmlValueType.CombatModType)]
    public void A_composite_is_copied_into_the_buffer(XmlValueType type)
    {
        Assert.True(EngineTextLimits.ValueIsCopiedToBuffer(type));
    }

    /// <summary>
    ///     The count is the check. 50 of 83 was read off the jump table; if this set stops being 50
    ///     someone has edited it by hand rather than re-measuring.
    /// </summary>
    [Fact]
    public void Exactly_fifty_type_codes_copy_into_the_buffer()
    {
        var copied = Enum.GetValues<XmlValueType>()
            .Where(EngineTextLimits.ValueIsCopiedToBuffer)
            .ToList();

        Assert.Equal(50, copied.Count);
    }

    /// <summary>
    ///     <c>HardPoints</c> is the tag the limit is actually reached through, and it is a
    ///     <c>NameReferenceList</c> - so the narrowing must not lose the case that motivated the
    ///     rule.
    /// </summary>
    [Fact]
    public void The_tag_that_reaches_the_limit_in_practice_is_still_covered()
    {
        Assert.True(EngineTextLimits.ValueIsCopiedToBuffer(XmlValueType.NameReferenceList));
    }
}