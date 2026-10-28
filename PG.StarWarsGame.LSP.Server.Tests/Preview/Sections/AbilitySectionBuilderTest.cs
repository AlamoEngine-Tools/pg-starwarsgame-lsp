// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Server.Preview;
using PG.StarWarsGame.LSP.Server.Preview.Sections;
using PG.StarWarsGame.LSP.Xml.Validation;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview.Sections;

/// <summary>
///     The ability section on its own (#314): rows out of <c>Unit_Abilities_Data</c> with their
///     timing, modifiers, deploy clips and the particle proxies bound to them.
/// </summary>
public sealed class AbilitySectionBuilderTest
{
    private const string TwoAbilities = """
                                        <Unit_Abilities_Data>
                                            <Unit_Ability>
                                                <Type>DEPLOY</Type>
                                                <Recharge_Seconds>45</Recharge_Seconds>
                                                <Expiration_Seconds>10</Expiration_Seconds>
                                                <Mod_Multiplier>SHIELD_REGEN, 2.5</Mod_Multiplier>
                                                <Mod_Multiplier>SPEED, x</Mod_Multiplier>
                                            </Unit_Ability>
                                            <Unit_Ability>
                                                <Type>TURBO</Type>
                                                <Owner_Attachment_Bone>B_Engine</Owner_Attachment_Bone>
                                            </Unit_Ability>
                                        </Unit_Abilities_Data>
                                        """;

    private static EffectiveObject Unit(string fragment)
    {
        var tagSource = new FakeVariantTagSource().With("Unit",
            new VariantTag("Unit_Abilities_Data", string.Empty, fragment, 0));
        var index = GameIndex.Empty with
        {
            WorkspaceDefinitions = ImmutableDictionary<string, ImmutableArray<GameSymbol>>.Empty.Add("Unit",
            [
                new GameSymbol("Unit", GameSymbolKind.XmlObject, "SpaceUnit",
                    new FileOrigin("file:///unit.xml", 0, 0), null, null)
            ])
        };
        return new EffectiveObjectResolver(index, new SchemaProviderBuilder().Build(), tagSource).Resolve("Unit");
    }

    private static PreviewParticle Particle(string bone)
    {
        return new PreviewParticle(bone, "Effect.alo", "hull", bone, 0, PreviewParticleGate.Always, null, true);
    }

    [Fact]
    public void Abilities_ReadEveryRow_WithTimingModifiersAndDeployClips()
    {
        var problems = new List<PreviewProblem>();

        var abilities = new AbilitySectionBuilder(TestCatalogues.English).Abilities(Unit(TwoAbilities), [],
            ["unit_idle_00", "unit_deploy_00", "unit_undeploy_00"], problems);

        Assert.Equal(["DEPLOY", "TURBO"], abilities.Select(a => a.Type));
        var deploy = abilities[0];
        Assert.Equal(45f, deploy.RechargeSeconds);
        Assert.Equal(10f, deploy.ExpirationSeconds);
        Assert.Equal([new PreviewAbilityModifier("SHIELD_REGEN", 2.5f)], deploy.Modifiers);
        Assert.Equal("unit_deploy_00", deploy.DeployClip);
        Assert.Equal("unit_undeploy_00", deploy.UndeployClip);
        Assert.Equal("B_Engine", abilities[1].OwnerAttachmentBone);
        Assert.Empty(problems);
    }

    /// <summary>The prefix table binds <c>PTE_</c> to TURBO; a <c>PRS_</c> proxy on a unit with no MISSILE_SHIELD is reported.</summary>
    [Fact]
    public void Abilities_BindTheParticlesWhosePrefixNamesThem_AndNoticeTheOrphans()
    {
        var problems = new List<PreviewProblem>();

        var abilities = new AbilitySectionBuilder(TestCatalogues.English).Abilities(Unit(TwoAbilities),
            [Particle("PTE_Engine_00"), Particle("PTE_Engine_01"), Particle("PRS_Shield"), Particle("p_smoke")], [],
            problems);

        Assert.Empty(abilities[0].ProxyNames);
        Assert.Equal(["PTE_Engine_00", "PTE_Engine_01"], abilities[1].ProxyNames);
        var problem = Assert.Single(problems);
        Assert.Equal(DiagnosticIds.PreviewUnboundAbilityEffect, problem.DiagnosticId);
        Assert.Contains("PRS_Shield", problem.Message);
    }

    [Fact]
    public void Abilities_AreEmpty_WhenTheUnitDeclaresNone()
    {
        Assert.Empty(new AbilitySectionBuilder(TestCatalogues.English)
            .Abilities(Unit("<Unit_Abilities_Data/>"), [], [], []));
    }
}
