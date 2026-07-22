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
///     #53: a hardpoint declared destroyable but with no Attachment_Bone cannot be attached to the
///     parent model and becomes indestructible - a gameplay bug, not a cosmetic one. Decidable from
///     the document alone, which is why it is separated from the bone-exists-on-model checks that
///     need .alo data.
///     <para>
///         The Is_Destroyable condition is load-bearing: across vanilla EaW and FoC all 133 hardpoints
///         without an Attachment_Bone are explicitly Is_Destroyable&gt;No, and no destroyable hardpoint
///         is missing one. Without it this rule would error on a quarter of the shipped hardpoints.
///     </para>
/// </summary>
public sealed class HardpointAttachmentBoneRuleTest
{
    private const string Uri = "file:///xml/Hardpoints.xml";

    private static IReadOnlyList<XmlFact> Produce(string xml)
    {
        var producer = new XmlDocumentFactProducer(
            new FileHelper(new MockFileSystem()),
            new EmptySchemaProvider(),
            new EmptyFileTypeRegistry(),
            new XmlStructuralValidator(),
            [new HardpointAttachmentBoneRule()]);
        return producer.Produce(xml, Uri);
    }

    [Fact]
    public void DestroyableHardpointWithAttachmentBone_EmitsNoFact()
    {
        const string xml =
            """<HardPoints><HardPoint Name="HP_A"><Is_Destroyable>Yes</Is_Destroyable><Attachment_Bone>HP_F-L_BONE</Attachment_Bone></HardPoint></HardPoints>""";

        Assert.Empty(Produce(xml).OfType<HardpointMissingAttachmentBoneFact>());
    }

    [Fact]
    public void DestroyableHardpointWithoutAttachmentBone_EmitsFactNamingIt()
    {
        const string xml =
            """<HardPoints><HardPoint Name="HP_Broken"><Is_Destroyable>Yes</Is_Destroyable><Health>350.0</Health></HardPoint></HardPoints>""";

        var fact = Assert.Single(Produce(xml).OfType<HardpointMissingAttachmentBoneFact>());
        Assert.Equal("HP_Broken", fact.HardpointId);
    }

    [Theory]
    [InlineData("No")]
    [InlineData("no")]
    [InlineData("False")]
    public void NonDestroyableHardpointWithoutAttachmentBone_EmitsNoFact(string destroyable)
    {
        // The vanilla case: 133 shipped hardpoints look exactly like this. They have nothing to
        // attach because they are not meant to come off, so the missing bone is correct.
        var xml =
            $"""<HardPoints><HardPoint Name="HP_Fixed"><Is_Destroyable>{destroyable}</Is_Destroyable><Fire_Bone_A>MuzzleA_00</Fire_Bone_A></HardPoint></HardPoints>""";

        Assert.Empty(Produce(xml).OfType<HardpointMissingAttachmentBoneFact>());
    }

    [Fact]
    public void UnspecifiedIsDestroyable_EmitsNoFact()
    {
        // Vanilla always states it; with the tag absent the engine default is unknown, so staying
        // silent beats guessing and flagging correct data.
        const string xml =
            """<HardPoints><HardPoint Name="HP_Unknown"><Health>350.0</Health></HardPoint></HardPoints>""";

        Assert.Empty(Produce(xml).OfType<HardpointMissingAttachmentBoneFact>());
    }

    [Fact]
    public void DestroyableHardpointWithEmptyAttachmentBone_IsTreatedAsMissing()
    {
        // Present-but-blank leaves the engine with nothing to attach to, same as absent.
        const string xml =
            """<HardPoints><HardPoint Name="HP_Blank"><Is_Destroyable>Yes</Is_Destroyable><Attachment_Bone>   </Attachment_Bone></HardPoint></HardPoints>""";

        var fact = Assert.Single(Produce(xml).OfType<HardpointMissingAttachmentBoneFact>());
        Assert.Equal("HP_Blank", fact.HardpointId);
    }

    [Fact]
    public void OnlyHardpointElementsAreChecked()
    {
        // Every other object type legitimately has no Attachment_Bone.
        const string xml =
            """<Root><SpaceUnit Name="Star_Destroyer"><Health>100</Health></SpaceUnit></Root>""";

        Assert.Empty(Produce(xml).OfType<HardpointMissingAttachmentBoneFact>());
    }

    [Fact]
    public void UnnamedHardpoint_EmitsNoFact()
    {
        // A hardpoint with no Name is a separate problem with its own diagnostic; reporting a
        // missing bone for something we cannot even name would just be noise.
        const string xml =
            """<HardPoints><HardPoint><Is_Destroyable>Yes</Is_Destroyable></HardPoint></HardPoints>""";

        Assert.Empty(Produce(xml).OfType<HardpointMissingAttachmentBoneFact>());
    }

    [Fact]
    public void SeveralHardpoints_OnlyTheOffendingOnesAreReported()
    {
        const string xml =
            """
            <HardPoints>
              <HardPoint Name="HP_Ok"><Is_Destroyable>Yes</Is_Destroyable><Attachment_Bone>B1</Attachment_Bone></HardPoint>
              <HardPoint Name="HP_Fixed"><Is_Destroyable>No</Is_Destroyable></HardPoint>
              <HardPoint Name="HP_Bad1"><Is_Destroyable>Yes</Is_Destroyable><Health>1</Health></HardPoint>
              <HardPoint Name="HP_Bad2"><Is_Destroyable>Yes</Is_Destroyable><Health>2</Health></HardPoint>
            </HardPoints>
            """;

        var facts = Produce(xml).OfType<HardpointMissingAttachmentBoneFact>().ToList();
        Assert.Equal(["HP_Bad1", "HP_Bad2"], facts.Select(f => f.HardpointId));
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
