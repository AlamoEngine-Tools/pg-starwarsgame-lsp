// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.IO.Abstractions.TestingHelpers;
using PG.StarWarsGame.LSP.Core.Util;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Tests.Workspace;

public sealed class ProjectRegistryTest
{
    private static readonly FileHelper Helper = new(new MockFileSystem());

    private static readonly string ModA = Path.Combine(Root(), "mods", "moda");
    private static readonly string ModB = Path.Combine(Root(), "mods", "modb");
    private static readonly string Shared = Path.Combine(Root(), "mods", "shared");

    private static string Root()
    {
        return Path.GetPathRoot(Path.GetFullPath("."))!;
    }

    private static string Uri(params string[] segments)
    {
        return Helper.PathToFileUri(Path.Combine(segments));
    }

    private static WorkspaceConfiguration Config(string projectDir, params string[] extraXmlDirs)
    {
        var xml = new List<string> { Path.Combine(projectDir, "data", "xml") };
        xml.AddRange(extraXmlDirs);
        var name = Path.GetFileName(projectDir);
        return new WorkspaceConfiguration(xml, [], [], [], null)
        {
            ProjectPath = Path.Combine(projectDir, name + ".pgproj").Replace('\\', '/').ToLowerInvariant(),
            Layers = [new ProjectLayer(0, name, xml, [], [], [], null)]
        };
    }

    [Fact]
    public void Primary_BeforeAnyProjectIsSet_IsAnEmptyDefaultWorkspace()
    {
        // Everything downstream assumes a workspace exists from construction: the pre-startup window
        // must behave exactly as it does today (no directories -> IsEaWXmlFile false), not crash.
        var registry = new ProjectRegistry(Helper);

        Assert.NotNull(registry.Primary);
        Assert.Single(registry.All);
        Assert.False(registry.Primary.XmlContext.HasDirectories);
    }

    [Fact]
    public void SetProjects_OneConfiguration_ProducesOneWorkspaceWithItsDirectoriesApplied()
    {
        var registry = new ProjectRegistry(Helper);

        registry.SetProjects([Config(ModA)]);

        var workspace = Assert.Single(registry.All);
        Assert.True(workspace.XmlContext.IsEaWXmlFile(Uri(ModA, "data", "xml", "units.xml")));
        Assert.Equal("moda", workspace.LayerMap.GetLayerName(0));
    }

    [Fact]
    public void SetProjects_Empty_KeepsAnEmptyDefaultWorkspace()
    {
        var registry = new ProjectRegistry(Helper);
        registry.SetProjects([Config(ModA)]);

        registry.SetProjects([]);

        Assert.Single(registry.All);
        Assert.False(registry.Primary.XmlContext.HasDirectories);
    }

    [Fact]
    public void SetProjects_TwoConfigurations_ProducesTwoIsolatedWorkspaces()
    {
        var registry = new ProjectRegistry(Helper);

        registry.SetProjects([Config(ModA), Config(ModB)]);

        Assert.Equal(2, registry.All.Count);
        var a = Assert.Single(registry.Resolve(Uri(ModA, "data", "xml", "units.xml")));
        var b = Assert.Single(registry.Resolve(Uri(ModB, "data", "xml", "units.xml")));
        Assert.NotSame(a, b);
        Assert.False(a.XmlContext.IsEaWXmlFile(Uri(ModB, "data", "xml", "units.xml")));
    }

    [Fact]
    public void Resolve_FileUnderNoProject_ReturnsEmpty()
    {
        var registry = new ProjectRegistry(Helper);
        registry.SetProjects([Config(ModA)]);

        Assert.Empty(registry.Resolve(Uri(Root(), "elsewhere", "notes.txt")));
    }

    [Fact]
    public void ResolvePrimary_FileUnderNoProject_FallsBackToPrimary()
    {
        // Mirrors ProjectLayerMap's "not under any known layer -> top rank" rule: an ad-hoc file
        // still gets answered rather than silently losing every language feature.
        var registry = new ProjectRegistry(Helper);
        registry.SetProjects([Config(ModA), Config(ModB)]);

        Assert.Same(registry.Primary, registry.ResolvePrimary(Uri(Root(), "elsewhere", "notes.txt")));
    }

    [Fact]
    public void Resolve_SharedDependency_ReturnsEveryOwningProjectLongestPrefixFirst()
    {
        // Two root projects referencing a common dependency both own its files. Multi-membership is
        // the normal case here, not an edge case.
        var sharedXml = Path.Combine(Shared, "data", "xml");
        var registry = new ProjectRegistry(Helper);
        registry.SetProjects([Config(ModA, sharedXml), Config(ModB, sharedXml)]);

        var owners = registry.Resolve(Uri(sharedXml, "units.xml"));

        Assert.Equal(2, owners.Count);
    }

    [Fact]
    public void Resolve_FileInsideTheProjectFolderButOutsideDeclaredDirectories_StillResolvesToIt()
    {
        var registry = new ProjectRegistry(Helper);
        registry.SetProjects([Config(ModA)]);

        var owner = Assert.Single(registry.Resolve(Uri(ModA, "readme.md")));

        Assert.Equal("moda", owner.LayerMap.GetLayerName(0));
    }

    [Fact]
    public void Resolve_NestedProjectDirectories_PrefersTheMostSpecificProject()
    {
        // A dependency checked out inside its dependent's folder: the inner project owns its own
        // files even though the outer project's directory also contains them.
        var inner = Path.Combine(ModA, "vendor", "shared");
        var registry = new ProjectRegistry(Helper);
        registry.SetProjects([Config(ModA), Config(inner)]);

        var owners = registry.Resolve(Uri(inner, "data", "xml", "units.xml"));

        Assert.Equal("shared", owners[0].LayerMap.GetLayerName(0));
    }

    [Fact]
    public void SetProjects_ReusesTheWorkspaceForAProjectThatIsStillPresent()
    {
        // A reload must not throw away the surviving projects' indexed state just because a sibling
        // project was added or removed.
        var registry = new ProjectRegistry(Helper);
        registry.SetProjects([Config(ModA)]);
        var before = registry.Primary;

        registry.SetProjects([Config(ModA), Config(ModB)]);

        Assert.Same(before, registry.Resolve(Uri(ModA, "data", "xml", "units.xml"))[0]);
    }

    [Fact]
    public void SetProjects_PreservesConfigurationOnEachWorkspace()
    {
        var registry = new ProjectRegistry(Helper);
        var config = Config(ModA);

        registry.SetProjects([config]);

        Assert.Same(config, registry.Primary.Configuration);
    }
}
