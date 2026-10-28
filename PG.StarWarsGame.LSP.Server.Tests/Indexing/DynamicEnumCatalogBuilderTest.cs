// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Indexing;
using PG.StarWarsGame.LSP.Testing;

namespace PG.StarWarsGame.LSP.Server.Tests.Indexing;

/// <summary>
///     The dynamic enum catalog on its own (#315): the values and their definitions read from the
///     enum files the schema names, found by filename under the xml roots, the root project's winning.
/// </summary>
public sealed class DynamicEnumCatalogBuilderTest
{
    private static string Root(string sub)
    {
        return Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, sub);
    }

    private static DynamicEnumCatalogBuilder Build(MockFileSystem fs, IGameIndexService svc,
        ISchemaProvider schema)
    {
        return new DynamicEnumCatalogBuilder(new FileHelper(fs), svc, schema,
            NullLogger<DynamicEnumCatalogBuilder>.Instance);
    }

    [Fact]
    public void Apply_ParsesEnumFile_AndPublishesValues()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml", "enum");
        var enumFilePath = Path.Combine(xmlDir, "surfacefxtriggertype.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [enumFilePath] =
                new("<EnumDefinition><GENERIC_TRACK>0</GENERIC_TRACK><MY_MOD_TRACK>99</MY_MOD_TRACK></EnumDefinition>")
        });
        var enumDef = new EnumDefinition
        {
            Name = "SurfaceFXTriggerType", Kind = EnumKind.DynamicXml,
            SourceFile = "surfacefxtriggertype.xml", Values = []
        };
        var schema = new SchemaProviderBuilder().WithEnum(enumDef).Build();
        var svc = new FakeGameIndexService();
        var builder = Build(fs, svc, schema);

        builder.Apply([xmlDir]);

        Assert.True(svc.Current.WorkspaceDynamicEnumValues.TryGetValue("SurfaceFXTriggerType", out var vals));
        Assert.Contains("GENERIC_TRACK", vals);
        Assert.Contains("MY_MOD_TRACK", vals);
    }

    [Fact]
    public void Apply_MissingEnumFile_PublishesEmptyDict()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml", "enum");
        var fs = new MockFileSystem();
        fs.AddDirectory(xmlDir);
        var enumDef = new EnumDefinition
        {
            Name = "SurfaceFXTriggerType", Kind = EnumKind.DynamicXml,
            SourceFile = "surfacefxtriggertype.xml", Values = []
        };
        var schema = new SchemaProviderBuilder().WithEnum(enumDef).Build();
        var svc = new FakeGameIndexService();
        var builder = Build(fs, svc, schema);

        builder.Apply([xmlDir]);

        Assert.Empty(svc.Current.WorkspaceDynamicEnumValues);
    }

    [Fact]
    public void Apply_SearchesByFilenameOnly_NotFullPath()
    {
        var root = Root("ws");
        // The enum file is directly in the xml root, not in an enum subdirectory.
        var xmlDir = Path.Combine(root, "data", "xml");
        var enumFilePath = Path.Combine(xmlDir, "surfacefxtriggertype.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [enumFilePath] = new("<EnumDefinition><CUSTOM>1</CUSTOM></EnumDefinition>")
        });
        var enumDef = new EnumDefinition
        {
            Name = "SurfaceFXTriggerType", Kind = EnumKind.DynamicXml,
            SourceFile = "surfacefxtriggertype.xml", Values = []
        };
        var schema = new SchemaProviderBuilder().WithEnum(enumDef).Build();
        var svc = new FakeGameIndexService();
        var builder = Build(fs, svc, schema);

        builder.Apply([xmlDir]);

        Assert.True(svc.Current.WorkspaceDynamicEnumValues.TryGetValue("SurfaceFXTriggerType", out var vals));
        Assert.Contains("CUSTOM", vals);
    }

    [Fact]
    public void Apply_RescanAfterEdit_PicksUpTheNewValue()
    {
        // The live path: the file gains a value on disk and the catalog is re-scanned. Adding a
        // value to a dynamic enum and having it stay "unknown" is the bug this guards.
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml", "enum");
        var enumFilePath = Path.Combine(xmlDir, "gameobjectpropertiestype.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [enumFilePath] = new("<EnumDefinition><Turret>0x1</Turret></EnumDefinition>")
        });
        var enumDef = new EnumDefinition
        {
            Name = "GameObjectPropertiesType", Kind = EnumKind.DynamicXml,
            SourceFile = "gameobjectpropertiestype.xml", Values = []
        };
        var schema = new SchemaProviderBuilder().WithEnum(enumDef).Build();
        var svc = new FakeGameIndexService();
        var builder = Build(fs, svc, schema);

        builder.Apply([xmlDir]);
        Assert.DoesNotContain("IsANewFlag",
            svc.Current.WorkspaceDynamicEnumValues["GameObjectPropertiesType"]);

        fs.File.WriteAllText(enumFilePath,
            "<EnumDefinition><Turret>0x1</Turret><IsANewFlag>0x2</IsANewFlag></EnumDefinition>");

        builder.Apply([xmlDir]);

        Assert.Contains("IsANewFlag",
            svc.Current.WorkspaceDynamicEnumValues["GameObjectPropertiesType"]);
    }

    [Fact]
    public void Apply_EnumFileInSubdirectory_IsFound()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var enumSubDir = Path.Combine(xmlDir, "enum");
        var enumFilePath = Path.Combine(enumSubDir, "surfacefxtriggertype.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [enumFilePath] = new("<EnumDefinition><GENERIC_TRACK>0</GENERIC_TRACK></EnumDefinition>")
        });
        var enumDef = new EnumDefinition
        {
            Name = "SurfaceFXTriggerType", Kind = EnumKind.DynamicXml,
            SourceFile = "surfacefxtriggertype.xml", Values = []
        };
        var schema = new SchemaProviderBuilder().WithEnum(enumDef).Build();
        var svc = new FakeGameIndexService();
        var builder = Build(fs, svc, schema);

        builder.Apply([xmlDir]); // pass parent dir, not the enum subdir

        Assert.True(svc.Current.WorkspaceDynamicEnumValues.TryGetValue("SurfaceFXTriggerType", out var vals));
        Assert.Contains("GENERIC_TRACK", vals);
    }

    [Fact]
    public void Apply_AnchorFormatEnum_PublishesEnumValueDefinitionsWithExactPosition()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml");
        var filePath = Path.Combine(xmlDir, "gameconstants.xml");
        const string xml = "<GameConstants><Damage_Types>EXPLOSIVE ENERGY</Damage_Types></GameConstants>";
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [filePath] = new(xml) });
        var enumDef = new EnumDefinition
        {
            Name = "DamageType", Kind = EnumKind.DynamicXml,
            SourceFile = "data/xml/gameconstants.xml$Damage_Types", Values = []
        };
        var schema = new SchemaProviderBuilder().WithEnum(enumDef).Build();
        var svc = new FakeGameIndexService();
        var builder = Build(fs, svc, schema);

        builder.Apply([xmlDir]);

        Assert.True(svc.Current.WorkspaceEnumValueDefinitions.TryGetValue("DamageType", out var valueMap));
        Assert.True(valueMap.TryGetValue("EXPLOSIVE", out var explosiveOrigin));
        Assert.True(explosiveOrigin.IsNavigable);
        Assert.True(valueMap.TryGetValue("ENERGY", out var energyOrigin));
        // ENERGY comes after EXPLOSIVE in the same line, so its column must be greater
        Assert.True(energyOrigin.Column > explosiveOrigin.Column);
    }

    [Fact]
    public void Apply_FileFormatEnum_PublishesEnumValueDefinitions()
    {
        var root = Root("ws");
        var xmlDir = Path.Combine(root, "data", "xml", "enum");
        var enumFilePath = Path.Combine(xmlDir, "surfacefxtriggertype.xml");
        const string xml = """
                           <EnumDefinition>
                               <GENERIC_TRACK>0</GENERIC_TRACK>
                               <MY_MOD_TRACK>99</MY_MOD_TRACK>
                           </EnumDefinition>
                           """;
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [enumFilePath] = new(xml) });
        var enumDef = new EnumDefinition
        {
            Name = "SurfaceFXTriggerType", Kind = EnumKind.DynamicXml,
            SourceFile = "surfacefxtriggertype.xml", Values = []
        };
        var schema = new SchemaProviderBuilder().WithEnum(enumDef).Build();
        var svc = new FakeGameIndexService();
        var builder = Build(fs, svc, schema);

        builder.Apply([xmlDir]);

        Assert.True(svc.Current.WorkspaceEnumValueDefinitions
            .TryGetValue("SurfaceFXTriggerType", out var valueMap));
        Assert.True(valueMap.TryGetValue("GENERIC_TRACK", out var origin));
        Assert.True(origin.IsNavigable);
        Assert.True(origin.Line > 0); // must be inside the file, past the root element
    }

    [Fact]
    public void Apply_RootProjectOverridesDependency_UsesRootValues()
    {
        // dep has surfacefxtriggertype.xml with DEP_TRACK; root has it with ROOT_TRACK.
        // config.XmlDirectories is [depXml, rootXml] (deps-first, root-last).
        // Root's file must win because it has the higher layer rank.
        var depXml = Path.Combine(Root("dep"), "data", "xml");
        var rootXml = Path.Combine(Root("root"), "data", "xml");
        var depFile = Path.Combine(depXml, "surfacefxtriggertype.xml");
        var rootFile = Path.Combine(rootXml, "surfacefxtriggertype.xml");
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [depFile] = new("<EnumDefinition><DEP_TRACK>0</DEP_TRACK></EnumDefinition>"),
            [rootFile] = new("<EnumDefinition><ROOT_TRACK>0</ROOT_TRACK></EnumDefinition>")
        });
        var enumDef = new EnumDefinition
        {
            Name = "SurfaceFXTriggerType", Kind = EnumKind.DynamicXml,
            SourceFile = "surfacefxtriggertype.xml", Values = []
        };
        var schema = new SchemaProviderBuilder().WithEnum(enumDef).Build();
        var svc = new FakeGameIndexService();
        var builder = Build(fs, svc, schema);

        // Pass roots in deps-first / root-last order (same as ModProjectResolver produces).
        builder.Apply([depXml, rootXml]);

        var vals = svc.Current.WorkspaceDynamicEnumValues["SurfaceFXTriggerType"];
        Assert.Contains("ROOT_TRACK", vals); // root project's value is present
        Assert.DoesNotContain("DEP_TRACK", vals); // dependency value is suppressed by override
    }
}