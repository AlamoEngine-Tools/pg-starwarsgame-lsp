// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Xml.Validation.Handlers;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;

/// <summary>
///     A grenade or remote-bomb ability must name a projectile that IS a grenade.
/// </summary>
/// <remarks>
///     <para>
///         From the assert seam - there is no engine message.
///         <c>GrenadeAttackAbility.cpp:440</c> and <c>RemoteBombAbility.cpp:388</c> both read
///         <c>!type-&gt;Is_Projectile_Grenade()</c>, and both are FAIL_IF-shaped: the assert fires
///         when the answer is FALSE and the function then returns false. So the text states the
///         failure and the rule is the opposite of how it reads.
///     </para>
///     <para>
///         <c>Is_Projectile_Grenade()</c> is one comparison -
///         <c>ProjCategory == PROJECTILE_CATEGORY_GRENADE</c> - so the property to check on the
///         target is its <c>Projectile_Category</c>.
///     </para>
///     <para>
///         Scoped per (owner, tag), not per tag: <c>Bomb_Type</c> is declared on three ability
///         types and only <c>RemoteBombAbility</c> demands a grenade.
///     </para>
///     <para>
///         Measured on the corpus: 10 declarations across <c>eaw/</c> and <c>foc/</c>, zero
///         violations. Four of them name a projectile that carries no
///         <c>Projectile_Category</c> of its own and inherits one through
///         <c>Variant_Of_Existing_Type</c> - which is why this reads the EFFECTIVE object rather
///         than the node, and why a naive check would have reported Mara Jade and Kyle Katarn.
///     </para>
/// </remarks>
public sealed class GrenadeProjectileHandlerTest
{
    private static readonly GrenadeProjectileHandler Sut = new();

    [Theory]
    [InlineData("Grenade_Attack_Ability", "Grenade_Type")]
    [InlineData("Remote_Bomb_Ability", "Bomb_Type")]
    public void A_projectile_that_is_not_a_grenade_is_reported(string owner, string tag)
    {
        var ctx = CtxWith(("Proj_Laser", "LASER"));

        var d = Assert.Single(Sut.Handle(Fact(owner, tag, "Proj_Laser"), ctx));

        Assert.Equal(XmlDiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Proj_Laser", d.Message);
        Assert.Contains("GRENADE", d.Message);
    }

    [Theory]
    [InlineData("Grenade_Attack_Ability", "Grenade_Type")]
    [InlineData("Remote_Bomb_Ability", "Bomb_Type")]
    public void A_grenade_projectile_is_silent(string owner, string tag)
    {
        Assert.Empty(Sut.Handle(Fact(owner, tag, "Proj_Sticky_Bomb"),
            CtxWith(("Proj_Sticky_Bomb", "GRENADE"))));
    }

    /// <summary>The engine uppercases every name it resolves, so the value's casing is not ours to police.</summary>
    [Fact]
    public void The_category_is_matched_case_insensitively()
    {
        Assert.Empty(Sut.Handle(Fact("Grenade_Attack_Ability", "Grenade_Type", "P"),
            CtxWith(("P", "Grenade"))));
    }

    /// <summary>
    ///     The case four of the ten shipped declarations are in: the category is inherited rather
    ///     than written, and the effective object is what the engine sees.
    /// </summary>
    [Fact]
    public void A_category_inherited_through_a_variant_counts()
    {
        var ctx = XmlHandlerTestFixtures.EmptyCtx with
        {
            Objects = new FakeProjectiles(("Proj_Mara_Jade_Sticky_Bomb", "GRENADE", VariantProvenance.Inherited))
        };

        Assert.Empty(Sut.Handle(
            Fact("Grenade_Attack_Ability", "Grenade_Type", "Proj_Mara_Jade_Sticky_Bomb"), ctx));
    }

    /// <summary>
    ///     <c>Bomb_Type</c> is declared on three ability types and only one of them demands a
    ///     grenade - so the owner is part of the key, exactly as it is for the required-tag rules.
    /// </summary>
    [Theory]
    [InlineData("Demolition_Ability")]
    [InlineData("Cluster_Bomb_Ability")]
    public void Another_owner_of_Bomb_Type_is_not_this_rules_business(string owner)
    {
        Assert.Empty(Sut.Handle(Fact(owner, "Bomb_Type", "Proj_Laser"),
            CtxWith(("Proj_Laser", "LASER"))));
    }

    /// <summary>An id nothing defines belongs to the unresolved-reference check, not here.</summary>
    [Fact]
    public void An_unresolvable_projectile_is_left_to_the_reference_check()
    {
        Assert.Empty(Sut.Handle(Fact("Grenade_Attack_Ability", "Grenade_Type", "Nope"),
            CtxWith(("Something_Else", "GRENADE"))));
    }

    /// <summary>
    ///     A target with no category at all cannot be judged - saying it is not a grenade would be
    ///     inventing an answer the engine's own default may contradict.
    /// </summary>
    [Fact]
    public void A_target_with_no_category_is_silent()
    {
        var ctx = XmlHandlerTestFixtures.EmptyCtx with
        {
            Objects = new FakeProjectiles(("P", null, VariantProvenance.Own))
        };

        Assert.Empty(Sut.Handle(Fact("Grenade_Attack_Ability", "Grenade_Type", "P"), ctx));
    }

    /// <summary>Without a resolver the question cannot be answered, and silence is the honest answer.</summary>
    [Fact]
    public void Without_a_resolver_it_stays_quiet()
    {
        Assert.Empty(Sut.Handle(Fact("Grenade_Attack_Ability", "Grenade_Type", "Proj_Laser"),
            XmlHandlerTestFixtures.EmptyCtx));
    }

    [Fact]
    public void An_empty_value_is_left_to_the_required_tag_rule()
    {
        Assert.Empty(Sut.Handle(Fact("Grenade_Attack_Ability", "Grenade_Type", "   "),
            CtxWith(("Proj_Laser", "LASER"))));
    }

    private static XmlTagValueFact Fact(string owner, string tag, string value)
    {
        return new XmlTagValueFact("file:///abilities/Abilities.xml", 3, 4, value.Length,
            new XmlTagDefinition { Tag = tag, ValueType = XmlValueType.NameReference },
            value, owner);
    }

    private static DiagnosticsContext CtxWith(params (string Id, string Category)[] projectiles)
    {
        return XmlHandlerTestFixtures.EmptyCtx with
        {
            Objects = new FakeProjectiles(
                projectiles.Select(p => (p.Id, (string?)p.Category, VariantProvenance.Own)).ToArray())
        };
    }

    private sealed class FakeProjectiles(params (string Id, string? Category, VariantProvenance From)[] objects)
        : IEffectiveObjectSource
    {
        public EffectiveObject Resolve(string objectId)
        {
            var match = objects.FirstOrDefault(o =>
                string.Equals(o.Id, objectId, StringComparison.OrdinalIgnoreCase));

            if (match.Id is null)
                return new EffectiveObject(objectId, null, false, false, null,
                    ImmutableArray<string>.Empty, ImmutableArray<EffectiveTag>.Empty);

            var tags = match.Category is null
                ? ImmutableArray<EffectiveTag>.Empty
                : ImmutableArray.Create(new EffectiveTag("Projectile_Category", match.Category,
                    match.Category, match.From, match.Id, null));

            return new EffectiveObject(match.Id, "GameObjectType", true, false, null,
                ImmutableArray<string>.Empty, tags);
        }
    }
}