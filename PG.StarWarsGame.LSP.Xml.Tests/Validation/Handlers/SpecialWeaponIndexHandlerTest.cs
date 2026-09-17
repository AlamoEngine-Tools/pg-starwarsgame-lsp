// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     A faction's standalone special weapon needs a <c>Special_Weapon_Index</c> in 0 to 2 (issue #98).
/// </summary>
/// <remarks>
///     <para>
///         Measured in the 2018 build. Every reader of the faction's weapon - the space battle set-up,
///         the debug map load and the command bar - tests <c>Get_Special_Weapon_Index() &gt;= 0</c>,
///         and <c>GameModeClass::Add_Special_Weapon</c> also asserts the index is below
///         <c>Get_Max_Special_Weapons</c>, which returns 3. Outside that the weapon is not registered.
///         The index defaults to -1 in the type's constructor, so an object that never writes it fails.
///     </para>
///     <para>
///         None of them checks the description's "faction must be able to build object type". That is
///         not a rule and is not implemented.
///     </para>
///     <para>
///         A only. B is created the same way but has no command bar button, and is deprecated.
///     </para>
/// </remarks>
public sealed class SpecialWeaponIndexHandlerTest
{
    private static readonly SpecialWeaponIndexHandler Sut = new();

    private static XmlTagDefinition WeaponTag(string name = "Standalone_Space_Maps_Special_Weapon_A")
    {
        return new XmlTagDefinition
        {
            Tag = name,
            ValueType = XmlValueType.TypeReference,
            ReferenceKind = ReferenceKind.XmlObject,
            ReferenceTypeName = "GameObjectType"
        };
    }

    private static DiagnosticsContext CtxWith(string id, params (string Tag, string Value)[] tags)
    {
        return XmlHandlerTestFixtures.EmptyCtx with { Objects = new FakeObjects(id, tags) };
    }

    // Vanilla: Ground_Ion_Cannon 0, Ground_Empire_Hypervelocity_Gun 1, Ground_Magnepulse_Cannon 2.
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    public void Index_in_range_is_accepted(string index)
    {
        var fact = XmlHandlerTestFixtures.MakeFact(WeaponTag(), "W");

        Assert.Empty(Sut.Handle(fact, CtxWith("W", ("Special_Weapon_Index", index))));
    }

    [Fact]
    public void Missing_index_is_reported()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(WeaponTag(), "Ground_Barracks");

        var d = Assert.Single(Sut.Handle(fact, CtxWith("Ground_Barracks", ("Tactical_Health", "100"))).ToList());
        Assert.Equal(XmlDiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(DiagnosticIds.SpecialWeaponIndex, d.Id);
        Assert.Contains("Ground_Barracks", d.Message);
        Assert.Contains("no Special_Weapon_Index", d.Message);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("3")]
    [InlineData("12")]
    public void Index_out_of_range_is_reported(string index)
    {
        var fact = XmlHandlerTestFixtures.MakeFact(WeaponTag(), "W");

        var d = Assert.Single(Sut.Handle(fact, CtxWith("W", ("Special_Weapon_Index", index))).ToList());
        Assert.Contains($"Special_Weapon_Index {index}", d.Message);
        Assert.Contains("0 to 2", d.Message);
    }

    // The engine builds B, but the command bar reads only A, so B never gets a button and cannot be
    // fired. The tag's deprecation already says so; a second warning about a weapon nobody can use is noise.
    [Fact]
    public void Weapon_B_is_left_to_the_deprecation()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(WeaponTag("Standalone_Space_Maps_Special_Weapon_B"), "W");

        Assert.Empty(Sut.Handle(fact, CtxWith("W")));
    }

    // Not a number is the value validator's finding; guessing an index from it would be a second,
    // wrong diagnostic on the same typo.
    [Fact]
    public void Unparsable_index_is_left_to_the_value_check()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(WeaponTag(), "W");

        Assert.Empty(Sut.Handle(fact, CtxWith("W", ("Special_Weapon_Index", "one"))));
    }

    [Fact]
    public void Unknown_object_is_left_to_the_reference_check()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(WeaponTag(), "Does_Not_Exist");

        Assert.Empty(Sut.Handle(fact, CtxWith("Something_Else", ("Special_Weapon_Index", "-1"))));
    }

    // The engine reads an empty reference as no weapon and skips it.
    [Fact]
    public void Empty_value_is_ignored()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(WeaponTag(), "  ");

        Assert.Empty(Sut.Handle(fact, CtxWith("W")));
    }

    [Fact]
    public void No_object_source_stays_quiet()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(WeaponTag(), "W");

        Assert.Empty(Sut.Handle(fact, XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void Other_tags_are_ignored()
    {
        var fact = XmlHandlerTestFixtures.MakeFact(WeaponTag("Land_Bomber_Unit"), "W");

        Assert.Empty(Sut.Handle(fact, CtxWith("W")));
    }

    private sealed class FakeObjects(string id, (string Tag, string Value)[] tags) : IEffectiveObjectSource
    {
        public EffectiveObject Resolve(string objectId)
        {
            if (!string.Equals(id, objectId, StringComparison.OrdinalIgnoreCase))
                return new EffectiveObject(objectId, null, false, false, null,
                    ImmutableArray<string>.Empty, ImmutableArray<EffectiveTag>.Empty);

            return new EffectiveObject(id, "GameObjectType", true, false, null, [id],
                [..tags.Select(t => new EffectiveTag(t.Tag, t.Value, t.Value, VariantProvenance.Own, id, null))]);
        }
    }
}
