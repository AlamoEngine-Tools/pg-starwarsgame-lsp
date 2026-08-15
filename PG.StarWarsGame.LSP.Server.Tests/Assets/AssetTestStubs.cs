// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using PG.StarWarsGame.LSP.Core.Configuration;
using PG.StarWarsGame.LSP.Core.Workspace;
using PG.StarWarsGame.LSP.Server.Assets;
using PG.StarWarsGame.LSP.Server.Project;

namespace PG.StarWarsGame.LSP.Server.Tests.Assets;

/// <summary>
///     The collaborators an asset resolver needs, stood up with fixed answers.
/// </summary>
/// <remarks>
///     Shared rather than nested in one test, because the shader resolver needs exactly the same
///     pair and a second copy would be free to drift from the first.
/// </remarks>
internal sealed class StubConfiguration(LspConfiguration current) : ILspConfigurationProvider
{
    public LspConfiguration Current { get; } = current;

    public void LoadFrom(object? initializationOptions)
    {
    }
}

internal sealed class StubProjects(WorkspaceConfiguration? config) : IModProjectReloadService
{
    public IReadOnlyList<string>? LastAssetRoots => config?.AssetRoots;
    public WorkspaceConfiguration? LastWorkspaceConfig { get; } = config;
    public IReadOnlyList<string>? LastWorkspaceRoots => null;

    public Task LoadAsync(IEnumerable<string> workspaceRoots, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task ReloadAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task ReloadLocalisationAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

internal sealed class StubArchives(params (string Path, byte[] Bytes)[] entries) : IMegArchiveSet
{
    private readonly Dictionary<string, byte[]> _entries =
        entries.ToDictionary(e => e.Path, e => e.Bytes, StringComparer.OrdinalIgnoreCase);

    public int ArchiveCount => 1;

    public byte[]? TryRead(string normalizedPath)
    {
        return _entries.GetValueOrDefault(normalizedPath);
    }
}
