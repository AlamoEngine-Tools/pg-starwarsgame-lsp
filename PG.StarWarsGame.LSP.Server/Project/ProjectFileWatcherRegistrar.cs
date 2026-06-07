// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using PG.StarWarsGame.LSP.Core.Workspace;
using LspFileSystemWatcher = OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher;

namespace PG.StarWarsGame.LSP.Server.Project;

public static class ProjectFileWatcherRegistrar
{
    // Builds watched-file registration options from a workspace configuration. For Chunk 3 this
    // intentionally produces flat blanket globs (no RelativePattern); per-role scoped watchers are
    // a follow-up. *.pgproj is always registered so project-file changes trigger a reload.
    public static DidChangeWatchedFilesRegistrationOptions Build(WorkspaceConfiguration config)
    {
        _ = config;
        return new DidChangeWatchedFilesRegistrationOptions
        {
            Watchers = new Container<LspFileSystemWatcher>(
                new LspFileSystemWatcher { GlobPattern = "**/*.xml" },
                new LspFileSystemWatcher { GlobPattern = "**/*.lua" },
                new LspFileSystemWatcher { GlobPattern = "**/*.pgproj" })
        };
    }
}
