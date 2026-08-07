// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Immutable;
using PG.StarWarsGame.LSP.Core.Workspace;

namespace PG.StarWarsGame.LSP.Core.Symbols;

/// <summary>
///     The <see cref="IFileTypeRegistry" /> injected outside a project scope. Metafile registration
///     is per-project: the engine takes the first metafile it finds by name, so two root projects can
///     legitimately type the same shared file differently. Routing by URI lets each project keep its
///     own answer.
/// </summary>
public sealed class RoutedFileTypeRegistry : IFileTypeRegistry
{
    private readonly IProjectRegistry _registry;

    public RoutedFileTypeRegistry(IProjectRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>The union across projects. Keys collide only where projects agree on the file.</summary>
    public IReadOnlyDictionary<string, ImmutableArray<string>> All
    {
        get
        {
            var all = _registry.All;
            if (all.Count == 1) return all[0].FileTypes.All;

            var merged = new Dictionary<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var workspace in all)
            foreach (var (uri, types) in workspace.FileTypes.All)
                merged.TryAdd(uri, types);

            return merged;
        }
    }

    public ImmutableArray<string> GetTypesForFile(string fileUri)
    {
        foreach (var workspace in _registry.Resolve(fileUri))
        {
            var types = workspace.FileTypes.GetTypesForFile(fileUri);
            if (!types.IsDefaultOrEmpty) return types;
        }

        return _registry.Primary.FileTypes.GetTypesForFile(fileUri);
    }

    public void RegisterFile(string fileUri, ImmutableArray<string> typeNames)
    {
        _registry.ResolvePrimary(fileUri).FileTypes.RegisterFile(fileUri, typeNames);
    }

    public void UnregisterFile(string fileUri)
    {
        // Unregister everywhere: a deleted file must not stay typed in any project that saw it.
        foreach (var workspace in _registry.All)
            workspace.FileTypes.UnregisterFile(fileUri);
    }
}
