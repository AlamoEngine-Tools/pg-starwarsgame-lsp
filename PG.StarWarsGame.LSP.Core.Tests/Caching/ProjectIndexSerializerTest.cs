// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Caching;
using PG.StarWarsGame.LSP.Core.Symbols;

namespace PG.StarWarsGame.LSP.Core.Tests.Caching;

public sealed class ProjectIndexSerializerTest
{
    [Fact]
    public void RoundTrip_EmptySnapshot_PreservesValues()
    {
        var snapshot = new ProjectIndexSnapshot
        {
            SchemaVersion = ProjectIndexSnapshot.CurrentSchemaVersion,
            OverallHash = "abc123",
            DependencyHashes = [],
            Files = []
        };

        var bytes = ProjectIndexSerializer.Serialize(snapshot);
        var result = ProjectIndexSerializer.Deserialize(bytes);

        Assert.NotNull(result);
        Assert.Equal(ProjectIndexSnapshot.CurrentSchemaVersion, result.SchemaVersion);
        Assert.Equal("abc123", result.OverallHash);
        Assert.Empty(result.Files);
        Assert.Empty(result.DependencyHashes);
    }

    [Fact]
    public void RoundTrip_WithSymbolsReferencesAndGroupMemberships_PreservesAll()
    {
        var symbol = new GameSymbol("UNIT_A", GameSymbolKind.XmlObject, "GameObjectType",
            new FileOrigin("units.xml", 0, null), null);
        var reference = new GameReference("UNIT_A", GameSymbolKind.XmlObject, "GameObjectType",
            "file:///data/xml/faction.xml", 5, 10, 6);
        var groupMembership = new DocumentGroupMembership(
            new GroupMembership("GROUP_KEY", "GameObjectType", new FileOrigin("units.xml", 0, null)),
            3, 7, 9);

        var document = new SerializedDocument
        {
            Symbols = [symbol],
            References = [SerializedReference.FromRuntime(reference)],
            GroupMemberships = [SerializedDocumentGroupMembership.FromRuntime(groupMembership)],
            RequireArgs = ["lib/helper.lua"],
            LayerRank = 1,
            LayerName = "MyMod"
        };
        var snapshot = new ProjectIndexSnapshot
        {
            SchemaVersion = ProjectIndexSnapshot.CurrentSchemaVersion,
            OverallHash = "deadbeef",
            DependencyHashes =
            [
                new SerializedDependencyHash { ProjectPath = "/dep/dep.pgproj", OverallHash = "dep123" }
            ],
            Files =
            [
                new ProjectFileEntry
                {
                    RelativePath = "data/xml/units.xml", ContentHash = "sha256abc", Document = document
                }
            ]
        };

        var bytes = ProjectIndexSerializer.Serialize(snapshot);
        var result = ProjectIndexSerializer.Deserialize(bytes);

        Assert.NotNull(result);
        Assert.Equal("deadbeef", result.OverallHash);

        var file = Assert.Single(result.Files);
        Assert.Equal("data/xml/units.xml", file.RelativePath);
        Assert.Equal("sha256abc", file.ContentHash);

        var sym = Assert.Single(file.Document.Symbols);
        Assert.Equal("UNIT_A", sym.Id);
        Assert.Equal(GameSymbolKind.XmlObject, sym.Kind);
        Assert.Equal("GameObjectType", sym.TypeName);

        var refDto = Assert.Single(file.Document.References);
        Assert.Equal("UNIT_A", refDto.TargetId);
        Assert.Equal(GameSymbolKind.XmlObject, refDto.ExpectedKind);
        Assert.Equal(5, refDto.Line);
        Assert.Equal(10, refDto.Column);
        Assert.Equal(6, refDto.Length);

        Assert.Equal("lib/helper.lua", Assert.Single(file.Document.RequireArgs));
        Assert.Equal(1, file.Document.LayerRank);
        Assert.Equal("MyMod", file.Document.LayerName);

        var gm = Assert.Single(file.Document.GroupMemberships);
        Assert.Equal("GROUP_KEY", gm.Membership.GroupKey);
        Assert.Equal("GameObjectType", gm.Membership.MemberTypeName);
        Assert.Equal(3, gm.TagLine);
        Assert.Equal(7, gm.TagColumn);
        Assert.Equal(9, gm.TagLength);

        var dep = Assert.Single(result.DependencyHashes);
        Assert.Equal("/dep/dep.pgproj", dep.ProjectPath);
        Assert.Equal("dep123", dep.OverallHash);
    }

    [Fact]
    public void RoundTrip_DocumentIndex_ConvertionPreservesFields()
    {
        var symbol = new GameSymbol("LUA_GLOBAL", GameSymbolKind.LuaGlobal, null,
            new FileOrigin("script.lua", 2, null), "desc");
        var reference = new GameReference("OTHER", null, null, "file:///script.lua", 1, 0, 5);
        var doc = new DocumentIndex(
            "file:///data/xml/units.xml",
            7,
            ImmutableArray.Create(symbol),
            ImmutableArray.Create(reference),
            ImmutableArray.Create("lib.lua"),
            ImmutableArray<DocumentGroupMembership>.Empty,
            2,
            "RootMod");

        var entry = ProjectIndexSerializer.ToEntry("data/xml/units.xml", "hash999", doc);
        var restored = ProjectIndexSerializer.FromEntry(entry, "file:///data/xml/units.xml");

        Assert.Equal("file:///data/xml/units.xml", restored.DocumentUri);
        Assert.Equal(0, restored.Version); // version resets to 0
        Assert.Equal(2, restored.LayerRank);
        Assert.Equal("RootMod", restored.LayerName);
        Assert.Equal("LUA_GLOBAL", Assert.Single(restored.Symbols).Id);
        Assert.Equal("OTHER", Assert.Single(restored.References).TargetId);
        Assert.Equal("lib.lua", Assert.Single(restored.RequireArgs));
    }

    [Fact]
    public void Deserialize_WrongSchemaVersion_ReturnsNull()
    {
        var snapshot = new ProjectIndexSnapshot
        {
            SchemaVersion = ProjectIndexSnapshot.CurrentSchemaVersion + 99,
            OverallHash = "abc",
            DependencyHashes = [],
            Files = []
        };
        var bytes = ProjectIndexSerializer.Serialize(snapshot);

        var result = ProjectIndexSerializer.Deserialize(bytes);

        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_CorruptData_ReturnsNull()
    {
        var result = ProjectIndexSerializer.Deserialize([0x00, 0x01, 0x02, 0x03]);

        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_EmptyArray_ReturnsNull()
    {
        var result = ProjectIndexSerializer.Deserialize([]);

        Assert.Null(result);
    }
}