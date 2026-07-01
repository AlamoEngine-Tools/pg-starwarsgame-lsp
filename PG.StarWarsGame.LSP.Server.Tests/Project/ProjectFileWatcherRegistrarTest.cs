// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Project;

public sealed class ProjectFileWatcherRegistrarTest
{
    [Fact]
    public void Build_IncludesCsvAndPropertiesGlobs()
    {
        var options = ProjectFileWatcherRegistrar.Build(WorkspaceConfiguration.Empty);

        var globs = options.Watchers!.Select(w => w.GlobPattern).ToList();

        Assert.Contains("**/*.csv", globs);
        Assert.Contains("**/*.properties", globs);
    }

    [Fact]
    public void Build_StillIncludesXmlLuaAndPgprojGlobs()
    {
        var options = ProjectFileWatcherRegistrar.Build(WorkspaceConfiguration.Empty);

        var globs = options.Watchers!.Select(w => w.GlobPattern).ToList();

        Assert.Contains("**/*.xml", globs);
        Assert.Contains("**/*.lua", globs);
        Assert.Contains("**/*.pgproj", globs);
    }
}
