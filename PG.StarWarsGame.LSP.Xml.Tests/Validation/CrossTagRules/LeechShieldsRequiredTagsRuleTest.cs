// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Diagnostics;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Xml.Tests.Validation.Handlers;
using PG.StarWarsGame.LSP.Xml.Validation;
using PG.StarWarsGame.LSP.Xml.Validation.CrossTagRules;

namespace PG.StarWarsGame.LSP.Xml.Tests.Validation.CrossTagRules;

/// <summary>
///     The seven tags a <c>Leech_Shields_Ability</c> cannot run without.
/// </summary>
/// <remarks>
///     The scoping cases matter more than the happy path. <c>Beam_Texture_Name</c> is also used by
///     <c>Super_Laser_Ability</c> (the Eclipse) and <c>Beam_Frames</c> / <c>Beam_Width</c> by
///     <c>Energy_Weapon_Attack_Ability</c>, where the engine states no such requirement - a
///     tag-scoped rule would warn on units that are correctly configured.
/// </remarks>
public sealed class LeechShieldsRequiredTagsRuleTest
{
    private const string Uri = "file:///abilities/Abilities.xml";

    // Every tag the engine demands, as the one shipped instance (Kadalbe_Leech_Shields) sets them.
    private const string Complete = """
        <Shield_Damage_Per_Second>100.0</Shield_Damage_Per_Second>
        <Damage_Multiplier>3.0</Damage_Multiplier>
        <Beam_Bone_Name>HP_SHL_00_BONE</Beam_Bone_Name>
        <Beam_Frames>10</Beam_Frames>
        <Beam_Width>40.0</Beam_Width>
        <Beam_Texture_Name>tractor_beam00.tga</Beam_Texture_Name>
        <Beam_Effect_Name>Leech_Shields</Beam_Effect_Name>
        """;

    [Fact]
    public void A_complete_ability_is_silent()
    {
        Assert.Empty(Missing(Run($"<Leech_Shields_Ability Name='K'>{Complete}</Leech_Shields_Ability>")));
    }

    [Fact]
    public void Each_absent_tag_is_reported_once()
    {
        var body = Complete.Replace("<Beam_Width>40.0</Beam_Width>", string.Empty);

        var fact = Assert.Single(Missing(Run($"<Leech_Shields_Ability Name='K'>{body}</Leech_Shields_Ability>")));
        Assert.Equal("Beam_Width", fact.TagName);
        Assert.Equal("LeechShieldsAbility", fact.OwningType);
    }

    // The engine's complaint is that the value "has not been set"; blank is as unset as absent.
    [Fact]
    public void An_empty_tag_counts_as_unset()
    {
        var body = Complete.Replace("<Beam_Bone_Name>HP_SHL_00_BONE</Beam_Bone_Name>",
            "<Beam_Bone_Name>   </Beam_Bone_Name>");

        Assert.Equal("Beam_Bone_Name",
            Assert.Single(Missing(Run($"<Leech_Shields_Ability Name='K'>{body}</Leech_Shields_Ability>")))
                .TagName);
    }

    [Fact]
    public void An_empty_ability_reports_all_seven()
    {
        Assert.Equal(7, Missing(Run("<Leech_Shields_Ability Name='K'></Leech_Shields_Ability>")).Count);
    }

    // The scoping guard: the same tags on other ability types carry no such rule.
    [Theory]
    [InlineData("Super_Laser_Ability", "<Beam_Texture_Name>tractor_beam01.tga</Beam_Texture_Name>")]
    [InlineData("Energy_Weapon_Attack_Ability", "<Beam_Frames>5</Beam_Frames><Beam_Width>10</Beam_Width>")]
    [InlineData("Redirect_Blaster_Ability", "<Block_Chance>0.3</Block_Chance>")]
    public void Other_ability_types_are_untouched(string element, string body)
    {
        Assert.Empty(Missing(Run($"<{element} Name='X'>{body}</{element}>")));
    }

    private static IReadOnlyList<MissingRequiredTagFact> Missing(IEnumerable<XmlFact> facts)
    {
        return facts.OfType<MissingRequiredTagFact>().ToList();
    }

    private static IReadOnlyList<XmlFact> Run(string body)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            [new LeechShieldsRequiredTagsRule()]);

        return producer.Produce($"<Root>{body}</Root>", Uri);
    }
}

file sealed class EmptyFileTypeRegistry : IFileTypeRegistry
{
    public IReadOnlyDictionary<string, ImmutableArray<string>> All =>
        new Dictionary<string, ImmutableArray<string>>();

    public ImmutableArray<string> GetTypesForFile(string _)
    {
        return ImmutableArray<string>.Empty;
    }

    public void RegisterFile(string fileUri, ImmutableArray<string> typeNames)
    {
    }

    public void UnregisterFile(string fileUri)
    {
    }
}
