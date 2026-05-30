// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Schema.Providers;

namespace PG.StarWarsGame.LSP.Schema.Tests;

public sealed class LocalFileSchemaProviderTest : IDisposable
{
    // ── MockFileSystem tests ─────────────────────────────────────────────────

    private static readonly string MockSchemaRoot =
        Path.Combine(Path.GetPathRoot(Path.GetFullPath("."))!, "mock-schema");

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public LocalFileSchemaProviderTest()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
        }
    }

    private LocalFileSchemaProvider CreateAndLoad(
        string? tagsYaml = null, string? tagsFileName = "tags.yaml",
        string? tagsYaml2 = null, string? tagsFileName2 = null,
        string? typesYaml = null,
        string? enumsYaml = null, string? enumsFileName = "enum.yaml")
    {
        var tagsDir = Path.Combine(_tempDir, "tags");
        if (tagsYaml is not null)
        {
            Directory.CreateDirectory(tagsDir);
            File.WriteAllText(Path.Combine(tagsDir, tagsFileName!), tagsYaml);
        }

        if (tagsYaml2 is not null)
        {
            Directory.CreateDirectory(tagsDir);
            File.WriteAllText(Path.Combine(tagsDir, tagsFileName2!), tagsYaml2);
        }

        if (typesYaml is not null)
            File.WriteAllText(Path.Combine(_tempDir, "types.yaml"), typesYaml);

        if (enumsYaml is not null)
        {
            var enumsDir = Path.Combine(_tempDir, "enums");
            Directory.CreateDirectory(enumsDir);
            File.WriteAllText(Path.Combine(enumsDir, enumsFileName!), enumsYaml);
        }

        // Constructor calls Load(); explicit call here ensures we re-read after all files are written
        var provider = new LocalFileSchemaProvider(_tempDir, new FileSystem(),
            NullLogger<LocalFileSchemaProvider>.Instance);
        provider.Load();
        return provider;
    }

    [Fact]
    public void ParseTagFile_NameReference_WithoutReferenceKind_HasNoneReferenceKind()
    {
        const string yaml = """
                            tags:
                              - tag: Overlap_Test
                                type: NameReference
                            """;

        using var provider = CreateAndLoad(yaml);

        var tag = provider.GetTag("Overlap_Test");

        Assert.NotNull(tag);
        Assert.Equal(XmlValueType.NameReference, tag.ValueType);
        Assert.Equal(ReferenceKind.None, tag.ReferenceKind);
        Assert.Null(tag.ObjectType);
    }

    [Fact]
    public void ParseTagFile_NameReferenceWithReferenceType_Resolved()
    {
        const string tagsYaml = """
                                tags:
                                  - tag: Affiliation
                                    type: NameReferenceList
                                    referenceKind: xmlObject
                                    referenceType: Faction
                                """;
        const string typesYaml = """
                                 types:
                                   - typeName: Faction
                                     nameTag: Name
                                 """;

        using var provider = CreateAndLoad(tagsYaml, typesYaml: typesYaml);

        var tag = provider.GetTag("Affiliation");

        Assert.NotNull(tag);
        Assert.Equal("Affiliation", tag.Tag);
        Assert.Equal(XmlValueType.NameReferenceList, tag.ValueType);
        Assert.Equal("Faction", tag.ObjectType?.TypeName);
    }

    [Fact]
    public void ParseTagFile_SFXEventReference_Resolved()
    {
        const string tagsYaml = """
                                tags:
                                  - tag: SFXEvent_Select
                                    type: SFXEventReference
                                    referenceKind: xmlObject
                                    referenceType: SFXEvent
                                """;
        const string typesYaml = """
                                 types:
                                   - typeName: SFXEvent
                                     nameTag: Name
                                 """;

        using var provider = CreateAndLoad(tagsYaml, typesYaml: typesYaml);

        var tag = provider.GetTag("SFXEvent_Select");

        Assert.NotNull(tag);
        Assert.Equal(XmlValueType.SFXEventReference, tag.ValueType);
        Assert.Equal("SFXEvent", tag.ObjectType?.TypeName);
    }

    [Fact]
    public void ParseTagFile_DynamicEnumValue_EnumResolved()
    {
        const string tagsYaml = """
                                tags:
                                  - tag: CategoryMask
                                    type: DynamicEnumValue
                                    referenceKind: enum
                                    enumName: GameObjectCategoryType
                                """;
        const string enumsYaml = """
                                 name: GameObjectCategoryType
                                 values:
                                   - name: GAMEOBJECT_WALKABLE
                                 """;

        using var provider = CreateAndLoad(tagsYaml, enumsYaml: enumsYaml);

        var tag = provider.GetTag("CategoryMask");

        Assert.NotNull(tag);
        Assert.Equal(XmlValueType.DynamicEnumValue, tag.ValueType);
        Assert.Equal("GameObjectCategoryType", tag.Enum?.Name);
    }

    [Fact]
    public void ParseTagFile_DeprecatedAndAvailableSince_Parsed()
    {
        const string yaml = """
                            tags:
                              - tag: Old_Tag
                                type: Float
                                deprecated: true
                                availableSince: "FoC 1.0"
                            """;

        using var provider = CreateAndLoad(yaml);

        var tag = provider.GetTag("Old_Tag");

        Assert.NotNull(tag);
        Assert.True(tag.Deprecated);
        Assert.Equal("FoC 1.0", tag.AvailableSince);
    }

    [Fact]
    public void ParseTypeFile_GameObjectType_Parsed()
    {
        const string yaml = """
                            types:
                              - typeName: GameObjectType
                                nameTag: Name
                            """;

        using var provider = CreateAndLoad(typesYaml: yaml);

        var type = provider.GetObjectType("GameObjectType");

        Assert.NotNull(type);
        Assert.Equal("GameObjectType", type.TypeName);
        Assert.Equal("Name", type.NameTag);
    }

    [Fact]
    public void NullNameTag_ParsedCorrectly()
    {
        const string yaml = """
                            types:
                              - typeName: GameConstants
                            """;

        using var provider = CreateAndLoad(typesYaml: yaml);

        var type = provider.GetObjectType("GameConstants");

        Assert.NotNull(type);
        Assert.Null(type.NameTag);
    }

    [Fact]
    public void LoadDirectory_MultipleYamlFiles_AllTagsAndTypesLoaded()
    {
        const string tagsYaml = """
                                tags:
                                  - tag: Tactical_Health
                                    type: Float
                                  - tag: Affiliation
                                    type: NameReferenceList
                                    referenceType: Faction
                                """;
        const string typesYaml = """
                                 types:
                                   - typeName: GameObjectType
                                     nameTag: Name
                                   - typeName: HardPoint
                                     nameTag: Name
                                   - typeName: Faction
                                     nameTag: Name
                                 """;

        using var provider = CreateAndLoad(tagsYaml, typesYaml: typesYaml);

        Assert.Equal(2, provider.AllTags.Count);
        Assert.Equal(3, provider.AllObjectTypes.Count);
    }

    [Fact]
    public void GetTag_CaseInsensitive_ReturnsTag()
    {
        const string yaml = """
                            tags:
                              - tag: Tactical_Health
                                type: Float
                            """;

        using var provider = CreateAndLoad(yaml);

        Assert.NotNull(provider.GetTag("tactical_health"));
        Assert.NotNull(provider.GetTag("TACTICAL_HEALTH"));
        Assert.NotNull(provider.GetTag("Tactical_Health"));
    }

    [Fact]
    public void UnknownType_IsSkipped_NoExceptionTagAbsent()
    {
        const string yaml = """
                            tags:
                              - tag: GoodTag
                                type: Float
                              - tag: BadTag
                                type: UnknownType
                            """;

        var ex = Record.Exception(() =>
        {
            using var provider = CreateAndLoad(yaml);
            var tag = Assert.Single(provider.AllTags);
            Assert.Equal("GoodTag", tag.Tag);
            Assert.Null(provider.GetTag("BadTag"));
        });

        Assert.Null(ex);
    }

    [Fact]
    public void EmptyDirectory_ReturnsEmptyCollections()
    {
        using var provider =
            new LocalFileSchemaProvider(_tempDir, new FileSystem(), NullLogger<LocalFileSchemaProvider>.Instance);
        provider.Load();

        Assert.Empty(provider.AllTags);
        Assert.Empty(provider.AllObjectTypes);
    }

    [Fact]
    public void GetTagsForType_ReturnsTagSetForNamedFile()
    {
        const string yaml = """
                            tags:
                              - tag: Tactical_Health
                                type: Float
                              - tag: Shield_Points
                                type: Float
                            """;

        using var provider = CreateAndLoad(yaml, "GameObjectType.yaml");

        Assert.Equal(2, provider.GetTagsForType("GameObjectType").Count);
        Assert.Empty(provider.GetTagsForType("Faction"));
    }

    [Fact]
    public void GetTagsForType_CaseInsensitive()
    {
        const string yaml = """
                            tags:
                              - tag: Tactical_Health
                                type: Float
                              - tag: Shield_Points
                                type: Float
                            """;

        using var provider = CreateAndLoad(yaml, "GameObjectType.yaml");

        Assert.Equal(2, provider.GetTagsForType("gameobjecttype").Count);
        Assert.Equal(2, provider.GetTagsForType("GAMEOBJECTTYPE").Count);
    }

    [Fact]
    public void GetAllTagDefinitions_ReturnsOneEntryPerType()
    {
        const string gameObjectTags = """
                                      tags:
                                        - tag: Text_ID
                                          type: NameReference
                                      """;
        const string factionTags = """
                                   tags:
                                     - tag: Text_ID
                                       type: NameReference
                                   """;

        using var provider = CreateAndLoad(
            gameObjectTags, "GameObjectType.yaml",
            factionTags, "Faction.yaml");

        Assert.Equal(2, provider.GetAllTagDefinitions("Text_ID").Count);
    }

    [Fact]
    public void AllMetafiles_WithMetaYaml_PopulatedAfterLoad()
    {
        const string metaYaml = """
                                metafiles:
                                  - path: data/xml/gameobjectfiles.xml
                                    metaFileType: fileRegistry
                                    types:
                                      - GameObjectType
                                  - path: data/xml/movies.xml
                                    metaFileType: directContent
                                    types:
                                      - BinkMovie
                                """;
        var metaDir = Path.Combine(_tempDir, "meta");
        Directory.CreateDirectory(metaDir);
        File.WriteAllText(Path.Combine(metaDir, "metafiles.yaml"), metaYaml);

        using var provider =
            new LocalFileSchemaProvider(_tempDir, new FileSystem(), NullLogger<LocalFileSchemaProvider>.Instance);
        provider.Load();

        Assert.Equal(2, provider.AllMetafiles.Count);
        var first = provider.AllMetafiles[0];
        Assert.Equal("data/xml/gameobjectfiles.xml", first.Path);
        Assert.Equal(MetafileType.FileRegistry, first.MetafileType);
        Assert.Equal(["GameObjectType"], first.Types);
    }

    [Fact]
    public void AllMetafiles_WithoutMetaYaml_ReturnsEmpty()
    {
        using var provider =
            new LocalFileSchemaProvider(_tempDir, new FileSystem(), NullLogger<LocalFileSchemaProvider>.Instance);
        provider.Load();

        Assert.Empty(provider.AllMetafiles);
    }

    [Fact]
    public void ValidationOverride_AllFields_Parsed()
    {
        const string yaml = """
                            tags:
                              - tag: Damage
                                type: Float
                                validationOverride:
                                  validationId: damage-nonzero
                                  mode: replace
                                  order: prepend
                            """;

        using var provider = CreateAndLoad(yaml);

        var tag = provider.GetTag("Damage");

        Assert.NotNull(tag);
        Assert.NotNull(tag.ValidationOverride);
        Assert.Equal("damage-nonzero", tag.ValidationOverride.ValidationId);
        Assert.Equal(ValidationOverrideMode.Replace, tag.ValidationOverride.Mode);
        Assert.Equal(ValidationOverrideOrder.Prepend, tag.ValidationOverride.Order);
    }

    [Fact]
    public void ValidationOverride_DefaultsApplied()
    {
        const string yaml = """
                            tags:
                              - tag: Damage
                                type: Float
                                validationOverride:
                                  validationId: damage-nonzero
                            """;

        using var provider = CreateAndLoad(yaml);

        var tag = provider.GetTag("Damage");

        Assert.NotNull(tag);
        Assert.NotNull(tag.ValidationOverride);
        Assert.Equal("damage-nonzero", tag.ValidationOverride.ValidationId);
        Assert.Equal(ValidationOverrideMode.Additive, tag.ValidationOverride.Mode);
        Assert.Equal(ValidationOverrideOrder.Append, tag.ValidationOverride.Order);
    }

    [Fact]
    public void ValidationOverride_Absent_IsNull()
    {
        const string yaml = """
                            tags:
                              - tag: Damage
                                type: Float
                            """;

        using var provider = CreateAndLoad(yaml);

        var tag = provider.GetTag("Damage");

        Assert.NotNull(tag);
        Assert.Null(tag.ValidationOverride);
    }

    private static WatchlessMockFileSystem MakeMockFs(Dictionary<string, MockFileData>? files = null)
    {
        return files is null ? new WatchlessMockFileSystem() : new WatchlessMockFileSystem(files);
    }

    [Fact]
    public void MockFileSystem_ValidSchema_LoadsTagsAndTypes()
    {
        const string tagsYaml = """
                                tags:
                                  - tag: Tactical_Health
                                    type: Float
                                """;
        const string typesYaml = """
                                 types:
                                   - typeName: GameObjectType
                                     nameTag: Name
                                 """;
        var fs = MakeMockFs(new Dictionary<string, MockFileData>
        {
            [Path.Combine(MockSchemaRoot, "tags", "GameObjectType.yaml")] = new(tagsYaml),
            [Path.Combine(MockSchemaRoot, "types.yaml")] = new(typesYaml)
        });

        using var provider = new LocalFileSchemaProvider(MockSchemaRoot, fs,
            NullLogger<LocalFileSchemaProvider>.Instance);

        Assert.Single(provider.AllTags);
        Assert.Equal("Tactical_Health", provider.AllTags[0].Tag);
        Assert.Single(provider.AllObjectTypes);
    }

    [Fact]
    public void MockFileSystem_EmptyDirectory_ReturnsEmptyCollections()
    {
        var fs = MakeMockFs();
        fs.Directory.CreateDirectory(MockSchemaRoot);

        using var provider = new LocalFileSchemaProvider(MockSchemaRoot, fs,
            NullLogger<LocalFileSchemaProvider>.Instance);

        Assert.Empty(provider.AllTags);
        Assert.Empty(provider.AllObjectTypes);
        Assert.Empty(provider.AllEnums);
    }

    // MockFileSystem.FileSystemWatcher is virtual; override it to return a no-op factory
    // so LocalFileSchemaProvider can be constructed without a real file-system watcher.
    private sealed class WatchlessMockFileSystem : MockFileSystem
    {
        private readonly NullFileSystemWatcherFactory _watcherFactory;

        public WatchlessMockFileSystem()
        {
            _watcherFactory = new NullFileSystemWatcherFactory(this);
        }

        public WatchlessMockFileSystem(IDictionary<string, MockFileData> files) : base(files)
        {
            _watcherFactory = new NullFileSystemWatcherFactory(this);
        }

        public override IFileSystemWatcherFactory FileSystemWatcher => _watcherFactory;
    }

    private sealed class NullFileSystemWatcherFactory : IFileSystemWatcherFactory
    {
        public NullFileSystemWatcherFactory(IFileSystem fs)
        {
            FileSystem = fs;
        }

        public IFileSystem FileSystem { get; }

        public IFileSystemWatcher New()
        {
            return new NullFileSystemWatcher(FileSystem);
        }

        public IFileSystemWatcher New(string path)
        {
            return new NullFileSystemWatcher(FileSystem);
        }

        public IFileSystemWatcher New(string path, string filter)
        {
            return new NullFileSystemWatcher(FileSystem);
        }

        public IFileSystemWatcher? Wrap(FileSystemWatcher? watcher)
        {
            return new NullFileSystemWatcher(FileSystem);
        }
    }

    private sealed class NullFileSystemWatcher : IFileSystemWatcher
    {
        public NullFileSystemWatcher(IFileSystem fs)
        {
            FileSystem = fs;
        }

        public IFileSystem FileSystem { get; }
        public IContainer? Container => null;
        public bool EnableRaisingEvents { get; set; }
        public string Filter { get; set; } = string.Empty;
        public Collection<string> Filters { get; } = [];
        public bool IncludeSubdirectories { get; set; }
        public int InternalBufferSize { get; set; }
        public NotifyFilters NotifyFilter { get; set; }
        public string Path { get; set; } = string.Empty;
        public ISite? Site { get; set; }
        public ISynchronizeInvoke? SynchronizingObject { get; set; }

        public void BeginInit()
        {
        }

        public void EndInit()
        {
        }

        public IWaitForChangedResult WaitForChanged(WatcherChangeTypes changeType)
        {
            return NullResult.Instance;
        }

        public IWaitForChangedResult WaitForChanged(WatcherChangeTypes changeType, int timeout)
        {
            return NullResult.Instance;
        }

        public IWaitForChangedResult WaitForChanged(WatcherChangeTypes changeType, TimeSpan timeout)
        {
            return NullResult.Instance;
        }

        public void Dispose()
        {
        }

        private sealed class NullResult : IWaitForChangedResult
        {
            public static readonly NullResult Instance = new();
            public WatcherChangeTypes ChangeType => 0;
            public string? Name => null;
            public string? OldName => null;
            public bool TimedOut => true;
        }

#pragma warning disable CS0067
        public event FileSystemEventHandler? Changed;
        public event FileSystemEventHandler? Created;
        public event FileSystemEventHandler? Deleted;
        public event ErrorEventHandler? Error;
        public event RenamedEventHandler? Renamed;
#pragma warning restore CS0067
    }
}