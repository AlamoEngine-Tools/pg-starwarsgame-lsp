// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace PG.StarWarsGame.LSP.Core.Workspace;

/// <summary>
///     The <see cref="IEaWXmlContext" /> the handlers inject: every question is already asked about a
///     file URI, so it can be answered by the project that owns that file without any call site
///     needing to know projects exist.
///     <para>
///         Reads are a union across owners - a file that belongs to two projects (a shared
///         dependency) is EaW XML if either says so. Writes belong to whoever is populating a
///         project and should go to that <see cref="ProjectWorkspace" /> directly; the write methods
///         here exist for the single-project setups and route as best they can.
///     </para>
/// </summary>
public sealed class RoutedEaWXmlContext : IEaWXmlContext
{
    private readonly IProjectRegistry _registry;

    public RoutedEaWXmlContext(IProjectRegistry registry)
    {
        _registry = registry;
    }

    public bool HasDirectories => _registry.All.Any(w => w.XmlContext.HasDirectories);

    public bool IsEaWXmlFile(string fileUri)
    {
        return Any(fileUri, (c, uri) => c.IsEaWXmlFile(uri));
    }

    public bool IsLeafFile(string fileUri)
    {
        return Any(fileUri, (c, uri) => c.IsLeafFile(uri));
    }

    public string? TryGetXmlRelativePath(string fileUri)
    {
        // The most specific owner comes first, so its xml root is the one the file is really
        // relative to.
        foreach (var workspace in _registry.Resolve(fileUri))
        {
            var relative = workspace.XmlContext.TryGetXmlRelativePath(fileUri);
            if (relative is not null) return relative;
        }

        return _registry.Primary.XmlContext.TryGetXmlRelativePath(fileUri);
    }

    public void AddDirectory(string absolutePath)
    {
        // Routable: the directory itself says which project it belongs to.
        _registry.ResolvePrimary(absolutePath).XmlContext.AddDirectory(absolutePath);
    }

    public void SetDirectories(IEnumerable<string> absolutePaths)
    {
        _registry.Primary.XmlContext.SetDirectories(absolutePaths);
    }

    public void SetLeafDirectories(IEnumerable<string> absolutePaths)
    {
        _registry.Primary.XmlContext.SetLeafDirectories(absolutePaths);
    }

    private bool Any(string fileUri, Func<EaWXmlContext, string, bool> predicate)
    {
        var owners = _registry.Resolve(fileUri);
        if (owners.Count == 0)
            // Outside every project. Ask the primary anyway so a single-project session behaves
            // exactly as it did before routing existed (its context answers for everything).
            return predicate(_registry.Primary.XmlContext, fileUri);

        foreach (var workspace in owners)
            if (predicate(workspace.XmlContext, fileUri))
                return true;

        return false;
    }
}
