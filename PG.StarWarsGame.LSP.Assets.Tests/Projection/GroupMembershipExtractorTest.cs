// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Assets.Projection;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Assets.Tests.Projection;

public sealed class GroupMembershipExtractorTest
{
    // ── Guard cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Extract_EmptyEntries_ReturnsEmpty()
    {
        var result = GroupMembershipExtractor.Extract([], _ => null, OverlapTestSchema);
        Assert.Empty(result);
    }

    [Fact]
    public void Extract_SchemaWithNoReferenceGroupTags_ReturnsEmpty()
    {
        const string xml = """
                           <SFXEvents>
                               <SFXEvent Name="Unit_X">
                                   <Overlap_Test>Group_X</Overlap_Test>
                               </SFXEvent>
                           </SFXEvents>
                           """;
        var result = GroupMembershipExtractor.Extract(
            [("Unit_X", "sfxevents.xml")], _ => xml, NoGroupSchema);

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_FileReaderReturnsNull_SkipsFile()
    {
        var result = GroupMembershipExtractor.Extract(
            [("Unit_X", "sfxevents.xml")], _ => null, OverlapTestSchema);

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_SfxEventWithNoOverlapTest_ReturnsEmpty()
    {
        const string xml = """
                           <SFXEvents>
                               <SFXEvent Name="Unit_X">
                                   <Volume>80</Volume>
                               </SFXEvent>
                           </SFXEvents>
                           """;
        var result = GroupMembershipExtractor.Extract(
            [("Unit_X", "sfxevents.xml")], _ => xml, OverlapTestSchema);

        Assert.Empty(result);
    }

    [Fact]
    public void Extract_EmptyOverlapTestValue_Skipped()
    {
        const string xml = """
                           <SFXEvents>
                               <SFXEvent Name="Unit_X">
                                   <Overlap_Test>   </Overlap_Test>
                               </SFXEvent>
                           </SFXEvents>
                           """;
        var result = GroupMembershipExtractor.Extract(
            [("Unit_X", "sfxevents.xml")], _ => xml, OverlapTestSchema);

        Assert.Empty(result);
    }

    // ── Single-group formation ────────────────────────────────────────────────

    [Fact]
    public void Extract_TwoSfxEventsWithSameOverlapTest_FormOneGroupWithTwoMembers()
    {
        const string xml = """
                           <SFXEvents>
                               <SFXEvent Name="AT_AT_Run">
                                   <Overlap_Test>Unit_AT_AT</Overlap_Test>
                               </SFXEvent>
                               <SFXEvent Name="AT_AT_Shoot">
                                   <Overlap_Test>Unit_AT_AT</Overlap_Test>
                               </SFXEvent>
                           </SFXEvents>
                           """;

        var result = GroupMembershipExtractor.Extract(
            [("AT_AT_Run", "sfxevents.xml"), ("AT_AT_Shoot", "sfxevents.xml")],
            _ => xml,
            OverlapTestSchema);

        var members = Assert.Contains("Unit_AT_AT", (IDictionary<string, ImmutableArray<GroupMembership>>)result);
        Assert.Equal(2, members.Length);
        Assert.All(members, m => Assert.Equal("Unit_AT_AT", m.GroupKey));
        Assert.All(members, m => Assert.Equal("SFXEvent", m.MemberTypeName));
    }

    [Fact]
    public void Extract_SfxEventsWithDifferentOverlapTests_FormSeparateGroups()
    {
        const string xml = """
                           <SFXEvents>
                               <SFXEvent Name="AT_AT_Run">
                                   <Overlap_Test>Unit_AT_AT</Overlap_Test>
                               </SFXEvent>
                               <SFXEvent Name="TIE_Engine">
                                   <Overlap_Test>Unit_TIE_Fighter</Overlap_Test>
                               </SFXEvent>
                           </SFXEvents>
                           """;

        var result = GroupMembershipExtractor.Extract(
            [("AT_AT_Run", "sfxevents.xml"), ("TIE_Engine", "sfxevents.xml")],
            _ => xml,
            OverlapTestSchema);

        Assert.Equal(2, result.Count);
        Assert.Contains("Unit_AT_AT", (IDictionary<string, ImmutableArray<GroupMembership>>)result);
        Assert.Contains("Unit_TIE_Fighter", (IDictionary<string, ImmutableArray<GroupMembership>>)result);
    }

    // ── Multi-file aggregation ────────────────────────────────────────────────

    [Fact]
    public void Extract_SameGroupKeyAcrossMultipleFiles_MembersAggregated()
    {
        const string file1 = """
                             <SFXEvents>
                                 <SFXEvent Name="AT_AT_Run">
                                     <Overlap_Test>Unit_AT_AT</Overlap_Test>
                                 </SFXEvent>
                             </SFXEvents>
                             """;
        const string file2 = """
                             <SFXEvents>
                                 <SFXEvent Name="AT_AT_Idle">
                                     <Overlap_Test>Unit_AT_AT</Overlap_Test>
                                 </SFXEvent>
                             </SFXEvents>
                             """;

        var result = GroupMembershipExtractor.Extract(
            [("AT_AT_Run", "file1.xml"), ("AT_AT_Idle", "file2.xml")],
            path => path == "file1.xml" ? file1 : path == "file2.xml" ? file2 : null,
            OverlapTestSchema);

        var members = Assert.Contains("Unit_AT_AT", (IDictionary<string, ImmutableArray<GroupMembership>>)result);
        Assert.Equal(2, members.Length);
    }

    [Fact]
    public void Extract_EachFileReadOnlyOnce_EvenForMultipleEntriesFromSameFile()
    {
        const string xml = """
                           <SFXEvents>
                               <SFXEvent Name="AT_AT_Run"><Overlap_Test>Unit_AT_AT</Overlap_Test></SFXEvent>
                           </SFXEvents>
                           """;
        var readCount = 0;

        GroupMembershipExtractor.Extract(
            [("AT_AT_Run", "sfxevents.xml"), ("AT_AT_Shoot", "sfxevents.xml")],
            path => { readCount++; return xml; },
            OverlapTestSchema);

        Assert.Equal(1, readCount);
    }

    // ── Membership origin ─────────────────────────────────────────────────────

    [Fact]
    public void Extract_MemberOriginCarriesFilePath()
    {
        const string xml = """
                           <SFXEvents>
                               <SFXEvent Name="AT_AT_Run">
                                   <Overlap_Test>Unit_AT_AT</Overlap_Test>
                               </SFXEvent>
                           </SFXEvents>
                           """;

        var result = GroupMembershipExtractor.Extract(
            [("AT_AT_Run", "data/xml/sfxevents.xml")],
            _ => xml,
            OverlapTestSchema);

        var member = Assert.Single(result["Unit_AT_AT"]);
        var origin = Assert.IsType<FileOrigin>(member.MemberOrigin);
        Assert.Equal("data/xml/sfxevents.xml", origin.Uri);
    }

    // ── Schema and helpers ────────────────────────────────────────────────────

    private static readonly ISchemaProvider NoGroupSchema = new GroupSchemaFake();

    private static readonly ISchemaProvider OverlapTestSchema = new GroupSchemaFake(
        new XmlTagDefinition
        {
            Tag = "Overlap_Test",
            ValueType = XmlValueType.NameReference,
            SemanticType = TagSemanticType.ReferenceGroup,
            ReferenceKind = ReferenceKind.XmlObject,
            ObjectType = new GameObjectTypeDefinition { TypeName = "SFXEvent", NameTag = "Name" }
        });
}

file sealed class GroupSchemaFake(params XmlTagDefinition[] tags) : ISchemaProvider
{
    public event EventHandler? SchemaRefreshed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<XmlTagDefinition> AllTags => tags;
    public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
    public IReadOnlyList<EnumDefinition> AllEnums => [];
    public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];
    public IReadOnlyList<MetafileDefinition> AllMetafiles => [];

    public XmlTagDefinition? GetTag(string tagName) => null;
    public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string tagName) => [];
    public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName) => [];
    public EnumDefinition? GetEnum(string enumName) => null;
    public GameObjectTypeDefinition? GetObjectType(string typeName) => null;
}
