// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Diagnostics.Suppression;
using PG.StarWarsGame.LSP.Core.Schema;
using PG.StarWarsGame.LSP.Core.Symbols;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Localisation;
using PG.StarWarsGame.LSP.Server.Project;
using PG.StarWarsGame.LSP.Server.Startup;
using PG.StarWarsGame.LSP.Server.Story;
using PG.StarWarsGame.LSP.Story.Dialog;

namespace PG.StarWarsGame.LSP.Server.Tests.Project;

/// <summary>
///     The guard for multi-root separation. Two projects open in one window must not be able to see
///     or overwrite each other's state, and that has to be enforced rather than remembered - the
///     failure mode is silent (mod B quietly gets mod A's settings, suppressions and layouts), so it
///     would not otherwise surface until a user reported it.
/// </summary>
public sealed class ProjectSeparationTest
{
    private static readonly string Root = Path.GetPathRoot(Path.GetFullPath("."))!;

    /// <summary>
    ///     Every service that is deliberately shared by all projects, with the reason. A service is
    ///     shared if and only if it is here.
    ///     <para>
    ///         <b>If this test fails you have changed what is shared across every open mod.</b> Add
    ///         the entry only when the state genuinely cannot differ per project - immutable
    ///         base-game data, or something window-global like the client connection - and say why in
    ///         <see cref="ProjectScopeFactory.SharedServices" />. Otherwise leave it per-project.
    ///     </para>
    /// </summary>
    private static readonly HashSet<string> ApprovedSharedServices = new(StringComparer.Ordinal)
    {
        nameof(ProjectScopeFactory.SharedServices.FileHelper),      // stateless path/URI helpers
        nameof(ProjectScopeFactory.SharedServices.Schema),          // immutable once loaded, same for every mod
        nameof(ProjectScopeFactory.SharedServices.Config),          // session config; window-wide by design
        nameof(ProjectScopeFactory.SharedServices.TextSource),      // one editor, one buffer per file
        nameof(ProjectScopeFactory.SharedServices.LoggerFactory),   // no state of its own
        nameof(ProjectScopeFactory.SharedServices.Notifier)         // one client connection
    };

    [Fact]
    public void SharedServices_MatchTheApprovedList()
    {
        var actual = typeof(ProjectScopeFactory.SharedServices)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var added = actual.Except(ApprovedSharedServices).OrderBy(n => n).ToList();
        var removed = ApprovedSharedServices.Except(actual).OrderBy(n => n).ToList();

        Assert.True(added.Count == 0,
            $"New shared service(s) [{string.Join(", ", added)}] would be shared by EVERY open mod. "
            + "Make them per-project, or add them to ApprovedSharedServices with a justification.");
        Assert.True(removed.Count == 0,
            $"Approved shared service(s) [{string.Join(", ", removed)}] no longer exist - update the list.");
    }

    [Theory]
    // Everything a project persists or scopes for itself. Each of these was a real cross-project
    // leak before the per-project provider existed.
    [InlineData(typeof(IStoryLayoutStore))]
    [InlineData(typeof(IWorkspaceSettingsStore))]
    [InlineData(typeof(IGlobalSuppressionStore))]
    [InlineData(typeof(IStoryDialogScope))]
    [InlineData(typeof(ILocalisationProjectRegistry))]
    [InlineData(typeof(ILocalisationLayerRegistry))]
    [InlineData(typeof(IGameIndexService))]
    [InlineData(typeof(IEaWXmlContext))]
    [InlineData(typeof(IProjectLayerMap))]
    [InlineData(typeof(IFileTypeRegistry))]
    public void PerProjectService_IsADistinctInstancePerProject(Type serviceType)
    {
        var registry = BuildRegistry();
        registry.SetProjects([Config("moda"), Config("modb")]);

        var a = registry.All[0].Services!.GetService(serviceType);
        var b = registry.All[1].Services!.GetService(serviceType);

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotSame(a, b);
    }

    [Fact]
    public void SharedService_IsTheSameInstanceForEveryProject()
    {
        // The counterpart of the test above: the expensive base-game state must NOT be duplicated,
        // which is the whole reason this is one server rather than one per folder.
        var registry = BuildRegistry();
        registry.SetProjects([Config("moda"), Config("modb")]);

        Assert.Same(
            registry.All[0].Services!.GetService(typeof(ISchemaProvider)),
            registry.All[1].Services!.GetService(typeof(ISchemaProvider)));
    }

    [Fact]
    public void Suppressions_PersistUnderTheirOwnProjectsAetswgDirectory()
    {
        var registry = BuildRegistry();
        registry.SetProjects([Config("moda"), Config("modb")]);

        var a = (IProjectContext)registry.All[0];
        var b = (IProjectContext)registry.All[1];

        Assert.NotNull(a.AetswgDirectory);
        Assert.NotNull(b.AetswgDirectory);
        Assert.NotEqual(a.AetswgDirectory, b.AetswgDirectory);
        Assert.Contains("moda", a.AetswgDirectory!);
        Assert.Contains("modb", b.AetswgDirectory!);
    }

    [Fact]
    public void SuppressingInOneProject_DoesNotSilenceTheOther()
    {
        var registry = BuildRegistry();
        registry.SetProjects([Config("moda"), Config("modb")]);
        var matcher = ParseMatcher("aetswg-001-0001");

        registry.All[0].Service<IGlobalSuppressionStore>().Add(matcher);

        Assert.Contains(matcher, registry.All[0].Service<IGlobalSuppressionStore>().GetAll());
        Assert.DoesNotContain(matcher, registry.All[1].Service<IGlobalSuppressionStore>().GetAll());
    }

    [Fact]
    public void RoutedSuppressionWrite_LandsInTheProjectOwningTheFile()
    {
        var registry = BuildRegistry();
        registry.SetProjects([Config("moda"), Config("modb")]);
        var routed = new RoutedGlobalSuppressionStore(registry);
        var matcher = ParseMatcher("aetswg-001-0002");

        routed.AddFor(Uri("modb", "data", "xml", "units.xml"), matcher);

        Assert.DoesNotContain(matcher, registry.All[0].Service<IGlobalSuppressionStore>().GetAll());
        Assert.Contains(matcher, registry.All[1].Service<IGlobalSuppressionStore>().GetAll());
    }

    [Fact]
    public void StoryLayout_WrittenForOneProject_IsNotVisibleToTheOther()
    {
        var registry = BuildRegistry();
        registry.SetProjects([Config("moda"), Config("modb")]);

        registry.All[0].Service<IStoryLayoutStore>()
            .Set("Campaign_A", [new StoryLayoutEntry("t.xml", "EVT", 1, 2)]);

        Assert.Single(registry.All[0].Service<IStoryLayoutStore>().Get("Campaign_A"));
        Assert.Empty(registry.All[1].Service<IStoryLayoutStore>().Get("Campaign_A"));
    }

    [Fact]
    public void DialogScope_OnlyClaimsFilesUnderItsOwnProject()
    {
        var registry = BuildRegistry();
        registry.SetProjects([Config("moda"), Config("modb")]);

        var inA = Uri("moda", "data", "dialog", "speech.txt");

        Assert.True(registry.All[0].Service<IStoryDialogScope>().IsInScope(inA));
        Assert.False(registry.All[1].Service<IStoryDialogScope>().IsInScope(inA));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static readonly FileHelper Helper = new(new MockFileSystem());

    private static string Uri(params string[] segments)
    {
        return Helper.NormalizeUri(Helper.PathToFileUri(Path.Combine([Root, "mods", .. segments])));
    }

    private static WorkspaceConfiguration Config(string name)
    {
        var dir = Path.Combine(Root, "mods", name);
        var xml = new List<string> { Path.Combine(dir, "data", "xml") };
        var dialog = new List<string> { Path.Combine(dir, "data", "dialog") };
        var pgproj = Path.Combine(dir, name + ".pgproj").Replace('\\', '/').ToLowerInvariant();

        return new WorkspaceConfiguration(xml, [], [], [], null)
        {
            ProjectPath = pgproj,
            StoryDialogRoots = dialog,
            Layers = [new ProjectLayer(0, name, xml, [], [], [], null, pgproj) { StoryDialogRoots = dialog }]
        };
    }

    private static ProjectRegistry BuildRegistry()
    {
        var fileHelper = new FileHelper(new MockFileSystem());
        var config = new FakeLspConfigurationProvider();
        var scopes = new ProjectScopeFactory(new ProjectScopeFactory.SharedServices
        {
            FileHelper = fileHelper,
            Schema = new SeparationTestSchemaProvider(),
            Config = config,
            TextSource = new SeparationTestTextSource(),
            LoggerFactory = NullLoggerFactory.Instance,
            Notifier = new SeparationTestNotifier()
        });
        return new ProjectRegistry(fileHelper, [], NullLoggerFactory.Instance, scopes.Create);
    }

    private static SuppressionMatcher ParseMatcher(string wire)
    {
        Assert.True(SuppressionMatcher.TryParse(wire, out var matcher), $"'{wire}' is not a matcher");
        return matcher;
    }

    private sealed class SeparationTestSchemaProvider : ISchemaProvider
    {
        public IReadOnlyList<MetafileDefinition> AllMetafiles => [];
        public IReadOnlyList<XmlTagDefinition> AllTags => [];
        public IReadOnlyList<GameObjectTypeDefinition> AllObjectTypes => [];
        public IReadOnlyList<EnumDefinition> AllEnums => [];
        public IReadOnlyList<HardcodedReferenceSet> AllHardcodedSets => [];

        public event EventHandler? SchemaRefreshed;

        public XmlTagDefinition? GetTag(string name)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetTagsForType(string typeName)
        {
            return [];
        }

        public GameObjectTypeDefinition? GetObjectType(string typeName)
        {
            return null;
        }

        public EnumDefinition? GetEnum(string name)
        {
            return null;
        }

        public HardcodedReferenceSet? GetHardcodedSet(string name)
        {
            return null;
        }

        public IReadOnlyList<XmlTagDefinition> GetAllTagDefinitions(string name)
        {
            return [];
        }
    }

    private sealed class SeparationTestNotifier : IUserNotifier
    {
        public void ShowError(string message)
        {
        }

        public void ShowInfo(string message)
        {
        }
    }

    private sealed class SeparationTestTextSource : IDocumentTextSource
    {
        public DocumentText? GetText(string uri)
        {
            return null;
        }
    }
}
